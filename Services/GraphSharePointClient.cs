using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Infrastructure;

namespace CandidatePortal.Api.Services;

public sealed class GraphSharePointClient(HttpClient httpClient, PortalOptions options) : ISharePointClient
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SharePointListResponse> listCache = new(StringComparer.OrdinalIgnoreCase);
    private string? accessToken;
    private DateTimeOffset tokenExpiresAt;
    private string? siteId = string.IsNullOrWhiteSpace(options.SharePointSiteId) ? null : options.SharePointSiteId;

    public object ConfigurationStatus() => new
    {
        configured = options.SharePointConfigured,
        site_url = EmptyToNull(options.SharePointSiteUrl),
        site_id_configured = !string.IsNullOrWhiteSpace(options.SharePointSiteId),
        portal_sync_enabled = options.SharePointSyncEnabled,
        storage_provider = options.StorageProvider,
        lists = new
        {
            candidates = options.SharePointCandidatesList,
            jobs = options.SharePointJobsList,
            applications = options.SharePointApplicationsList,
            recruitment_requests = options.SharePointRecruitmentRequestsList,
            cooperative_training = options.SharePointCooperativeTrainingList,
            cooperative_training_documents = options.SharePointCooperativeTrainingDocumentsLibrary,
            resumes = options.SharePointResumesLibrary,
        },
    };

    public async Task<object> DiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var tokenResult = new Dictionary<string, object?>
        {
            ["acquired"] = false,
            ["tenant_id"] = null,
            ["application_id"] = null,
            ["audience"] = null,
            ["application_roles"] = Array.Empty<string>(),
        };
        var siteAccess = new Dictionary<string, object?> { ["ok"] = false, ["site_id"] = null, ["error"] = null };
        try
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            tokenResult["acquired"] = true;
            try
            {
                var payload = token.Split('.')[1];
                payload += new string('=', (4 - payload.Length % 4) % 4);
                using var claims = JsonDocument.Parse(Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')));
                var root = claims.RootElement;
                tokenResult["tenant_id"] = StringClaim(root, "tid");
                tokenResult["application_id"] = StringClaim(root, "azp") ?? StringClaim(root, "appid");
                tokenResult["audience"] = StringClaim(root, "aud");
                tokenResult["application_roles"] = root.TryGetProperty("roles", out var roles)
                    ? roles.EnumerateArray().Select(role => role.GetString() ?? "").Order().ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                tokenResult["claims_error"] = "The access-token claims could not be decoded";
            }
        }
        catch (ApiException error)
        {
            tokenResult["error"] = error.Detail;
            return DiagnosticsResult(tokenResult, siteAccess);
        }

        try
        {
            siteAccess["site_id"] = await GetSiteIdAsync(cancellationToken);
            siteAccess["ok"] = true;
        }
        catch (ApiException error)
        {
            siteAccess["error"] = error.Detail;
        }
        return DiagnosticsResult(tokenResult, siteAccess);
    }

    public async Task<IReadOnlyList<SharePointListResponse>> ListListsAsync(CancellationToken cancellationToken = default)
    {
        var id = Uri.EscapeDataString(await GetSiteIdAsync(cancellationToken));
        var values = await GetCollectionAsync($"/sites/{id}/lists?$select=id,displayName,name,webUrl,list", cancellationToken);
        return values.Select(item => new SharePointListResponse(
            item.GetProperty("id").GetString()!,
            GetString(item, "displayName"), GetString(item, "name"), GetString(item, "webUrl"),
            item.TryGetProperty("list", out var list) ? GetString(list, "template") : null)).ToArray();
    }

    public Task<SharePointSetupResponse> ProvisionAsync(CancellationToken cancellationToken = default) =>
        SharePointProvisioner.ProvisionAsync(this, options, cancellationToken);

    public async Task<IReadOnlyList<SharePointItemResponse>> ListItemsAsync(string listName, CancellationToken cancellationToken = default)
    {
        var list = await GetListAsync(listName, cancellationToken);
        var site = Uri.EscapeDataString(await GetSiteIdAsync(cancellationToken));
        var listId = Uri.EscapeDataString(list.Id);
        var values = await GetCollectionAsync($"/sites/{site}/lists/{listId}/items?$expand=fields", cancellationToken);
        return values.Select(NormalizeItem).ToArray();
    }

    public async Task<SharePointItemResponse> GetItemAsync(string listName, int itemId, CancellationToken cancellationToken = default)
    {
        var list = await GetListAsync(listName, cancellationToken);
        var site = Uri.EscapeDataString(await GetSiteIdAsync(cancellationToken));
        var listId = Uri.EscapeDataString(list.Id);
        using var body = await SendAsync(HttpMethod.Get, $"/sites/{site}/lists/{listId}/items/{itemId}?$expand=fields", null, null, cancellationToken);
        return NormalizeItem(body?.RootElement ?? throw GraphError("SharePoint item was not found"));
    }

    public async Task<SharePointItemResponse?> FindItemByFieldAsync(string listName, string fieldName, string value, CancellationToken cancellationToken = default)
    {
        var expected = value.Trim();
        return (await ListItemsAsync(listName, cancellationToken)).FirstOrDefault(item =>
            item.Fields.TryGetValue(fieldName, out var actual) &&
            string.Equals(Convert.ToString(actual)?.Trim(), expected, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SharePointItemResponse> CreateItemAsync(string listName, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default)
    {
        var list = await GetListAsync(listName, cancellationToken);
        var site = Uri.EscapeDataString(await GetSiteIdAsync(cancellationToken));
        var listId = Uri.EscapeDataString(list.Id);
        using var created = await SendAsync(HttpMethod.Post, $"/sites/{site}/lists/{listId}/items", new { fields }, null, cancellationToken);
        if (created is null || !created.RootElement.TryGetProperty("id", out var idElement) ||
            !int.TryParse(idElement.GetString(), out var itemId))
        {
            throw GraphError("Microsoft Graph did not return the created item ID");
        }
        return await GetItemAsync(listName, itemId, cancellationToken);
    }

    public async Task<SharePointItemResponse> UpdateItemAsync(string listName, int itemId, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default)
    {
        if (fields.Count == 0)
        {
            return await GetItemAsync(listName, itemId, cancellationToken);
        }
        var list = await GetListAsync(listName, cancellationToken);
        var site = Uri.EscapeDataString(await GetSiteIdAsync(cancellationToken));
        var listId = Uri.EscapeDataString(list.Id);
        using var _ = await SendAsync(HttpMethod.Patch, $"/sites/{site}/lists/{listId}/items/{itemId}/fields", fields, null, cancellationToken);
        return await GetItemAsync(listName, itemId, cancellationToken);
    }

    public async Task DeleteItemAsync(string listName, int itemId, CancellationToken cancellationToken = default)
    {
        var list = await GetListAsync(listName, cancellationToken);
        var site = Uri.EscapeDataString(await GetSiteIdAsync(cancellationToken));
        var listId = Uri.EscapeDataString(list.Id);
        using var _ = await SendAsync(HttpMethod.Delete, $"/sites/{site}/lists/{listId}/items/{itemId}", null, null, cancellationToken);
    }

    public async Task<SharePointItemResponse> UploadResumeAsync(int candidateItemId, string candidateEmail, string filename, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var storedName = $"{candidateItemId}-{Guid.NewGuid():N}-{Path.GetFileName(filename)}";
        var (driveItem, listItemId) = await UploadLibraryFileAsync(options.SharePointResumesLibrary, options.SharePointDriveId, storedName, content, contentType, cancellationToken);
        var uploaded = await UpdateItemAsync(options.SharePointResumesLibrary, listItemId, new Dictionary<string, object?>
        {
            ["CandidateLookupId"] = candidateItemId.ToString(),
            ["CandidateEmail"] = candidateEmail.Trim().ToLowerInvariant(),
            ["UploadedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["IsCurrent"] = true,
        }, cancellationToken);
        uploaded.WebUrl = GetString(driveItem, "webUrl") ?? uploaded.WebUrl;
        foreach (var item in await ListItemsAsync(options.SharePointResumesLibrary, cancellationToken))
        {
            if (item.Id != uploaded.Id && FieldEquals(item, "CandidateEmail", candidateEmail) && FieldBoolean(item, "IsCurrent"))
            {
                await UpdateItemAsync(options.SharePointResumesLibrary, int.Parse(item.Id),
                    new Dictionary<string, object?> { ["IsCurrent"] = false }, cancellationToken);
            }
        }
        return uploaded;
    }

    public async Task<SharePointItemResponse> UploadCooperativeTrainingDocumentAsync(int requestItemId, string applicantEmail, string documentType, string filename, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        if (documentType is not ("Transcript" or "University Request"))
        {
            throw new ApiException(503, "Unsupported cooperative training document type");
        }
        var slug = documentType == "Transcript" ? "transcript" : "university-request";
        var storedName = $"training-{requestItemId}-{slug}-{Guid.NewGuid():N}-{Path.GetFileName(filename)}";
        var (driveItem, listItemId) = await UploadLibraryFileAsync(
            options.SharePointCooperativeTrainingDocumentsLibrary, "", storedName, content, contentType, cancellationToken);
        var uploaded = await UpdateItemAsync(options.SharePointCooperativeTrainingDocumentsLibrary, listItemId,
            new Dictionary<string, object?>
            {
                ["TrainingRequestLookupId"] = requestItemId.ToString(),
                ["DocumentType"] = documentType,
                ["ApplicantEmail"] = applicantEmail.Trim().ToLowerInvariant(),
                ["UploadedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["IsCurrent"] = true,
            }, cancellationToken);
        uploaded.WebUrl = GetString(driveItem, "webUrl") ?? uploaded.WebUrl;
        return uploaded;
    }

    public async Task<int> DeleteCooperativeTrainingDocumentsAsync(int requestItemId, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        foreach (var item in await ListItemsAsync(options.SharePointCooperativeTrainingDocumentsLibrary, cancellationToken))
        {
            var lookup = Field(item, "Training RequestLookupId") ?? Field(item, "TrainingRequestLookupId");
            if (Convert.ToString(lookup) == requestItemId.ToString())
            {
                await DeleteItemAsync(options.SharePointCooperativeTrainingDocumentsLibrary, int.Parse(item.Id), cancellationToken);
                deleted++;
            }
        }
        return deleted;
    }

    internal async Task<string> GetSiteIdAsync(CancellationToken cancellationToken)
    {
        if (siteId is not null) return siteId;
        ValidateConfiguration();
        if (!Uri.TryCreate(options.SharePointSiteUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ApiException(503, "SHAREPOINT_SITE_URL must be a valid HTTPS URL");
        }
        var path = uri.AbsolutePath.TrimEnd('/');
        var endpoint = path.Length == 0 ? $"/sites/{uri.Host}" : $"/sites/{uri.Host}:{path}";
        using var body = await SendAsync(HttpMethod.Get, endpoint + "?$select=id,displayName,webUrl", null, null, cancellationToken);
        siteId = body is null ? null : GetString(body.RootElement, "id");
        return siteId ?? throw GraphError("Microsoft Graph did not return a SharePoint site ID");
    }

    internal async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? json, HttpContent? content, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(method, path.StartsWith("https://", StringComparison.Ordinal) ? path : GraphBaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
            request.Content = content ?? (json is null ? null : JsonContent.Create(json));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                accessToken = null;
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw GraphError(await ErrorMessageAsync(response, cancellationToken));
            }
            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return null;
            }
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        throw GraphError("Microsoft Graph authentication failed after token refresh");
    }

    internal void ClearListCache() => listCache.Clear();

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (accessToken is not null && tokenExpiresAt > DateTimeOffset.UtcNow) return accessToken;
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (accessToken is not null && tokenExpiresAt > DateTimeOffset.UtcNow) return accessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(options.SharePointTenantId)}/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.SharePointClientId,
                    ["client_secret"] = options.SharePointClientSecret,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials",
                }),
            };
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw GraphError(await ErrorMessageAsync(response, cancellationToken));
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            accessToken = body.RootElement.GetProperty("access_token").GetString();
            var lifetime = body.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
            tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime - 60, 60));
            return accessToken ?? throw GraphError("Microsoft identity authentication failed");
        }
        finally { tokenLock.Release(); }
    }

    private async Task<SharePointListResponse> GetListAsync(string displayName, CancellationToken cancellationToken)
    {
        if (listCache.TryGetValue(displayName, out var cached)) return cached;
        var list = (await ListListsAsync(cancellationToken)).FirstOrDefault(value =>
            string.Equals(value.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        if (list is null) throw GraphError($"SharePoint list or library '{displayName}' was not found");
        listCache[displayName] = list;
        return list;
    }

    private async Task<IReadOnlyList<JsonElement>> GetCollectionAsync(string path, CancellationToken cancellationToken)
    {
        var result = new List<JsonElement>();
        string? next = path;
        while (next is not null)
        {
            using var body = await SendAsync(HttpMethod.Get, next, null, null, cancellationToken);
            if (body is null) break;
            if (body.RootElement.TryGetProperty("value", out var values))
            {
                result.AddRange(values.EnumerateArray().Select(value => value.Clone()));
            }
            next = GetString(body.RootElement, "@odata.nextLink");
        }
        return result;
    }

    private async Task<(JsonElement DriveItem, int ListItemId)> UploadLibraryFileAsync(string libraryName, string configuredDriveId, string storedName, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var driveId = await GetLibraryDriveIdAsync(libraryName, configuredDriveId, cancellationToken);
        using var byteContent = new ByteArrayContent(content);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        using var uploaded = await SendAsync(HttpMethod.Put,
            $"/drives/{Uri.EscapeDataString(driveId)}/root:/{Uri.EscapeDataString(storedName)}:/content", null, byteContent, cancellationToken);
        var item = uploaded?.RootElement.Clone() ?? throw GraphError("Microsoft Graph did not return the uploaded document ID");
        string? listItemId = null;
        if (item.TryGetProperty("sharepointIds", out var ids)) listItemId = GetString(ids, "listItemId");
        if (listItemId is null)
        {
            using var detail = await SendAsync(HttpMethod.Get,
                $"/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(GetString(item, "id")!)}/listItem?$select=id", null, null, cancellationToken);
            listItemId = detail is null ? null : GetString(detail.RootElement, "id");
        }
        return (item, int.Parse(listItemId ?? throw GraphError("Microsoft Graph did not return the document list item ID")));
    }

    private async Task<string> GetLibraryDriveIdAsync(string libraryName, string configuredDriveId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredDriveId)) return configuredDriveId;
        var site = Uri.EscapeDataString(await GetSiteIdAsync(cancellationToken));
        var drives = await GetCollectionAsync($"/sites/{site}/drives?$select=id,name,webUrl", cancellationToken);
        var drive = drives.FirstOrDefault(value => string.Equals(GetString(value, "name"), libraryName, StringComparison.OrdinalIgnoreCase));
        return GetString(drive, "id") ?? throw GraphError($"Document library '{libraryName}' does not have an accessible drive");
    }

    private void ValidateConfiguration()
    {
        if (!options.SharePointConfigured)
            throw new ApiException(503, "SharePoint is not configured. Set tenant, client, secret, and site URL or site ID.");
    }

    private static SharePointItemResponse NormalizeItem(JsonElement item) => new()
    {
        Id = GetString(item, "id") ?? "",
        WebUrl = GetString(item, "webUrl"),
        CreatedAt = GetDateTime(item, "createdDateTime"),
        UpdatedAt = GetDateTime(item, "lastModifiedDateTime"),
        Fields = item.TryGetProperty("fields", out var fields)
            ? fields.EnumerateObject().ToDictionary(property => property.Name, property => JsonValue(property.Value))
            : [],
    };

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        _ => value.Clone(),
    };

    private object DiagnosticsResult(Dictionary<string, object?> token, Dictionary<string, object?> siteAccess)
        => new
        {
            configured = options.SharePointConfigured,
            site_url = EmptyToNull(options.SharePointSiteUrl),
            configured_tenant_id = EmptyToNull(options.SharePointTenantId),
            configured_client_id = EmptyToNull(options.SharePointClientId),
            token,
            site_access = siteAccess,
        };

    private static string? StringClaim(JsonElement root, string name) => GetString(root, name);
    private static string? GetString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString() : null;
    private static DateTime? GetDateTime(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDateTime(out var parsed) ? parsed : null;
    private static object? Field(SharePointItemResponse item, string name) => item.Fields.TryGetValue(name, out var value) ? value : null;
    private static bool FieldEquals(SharePointItemResponse item, string name, string value) =>
        string.Equals(Convert.ToString(Field(item, name)), value.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool FieldBoolean(SharePointItemResponse item, string name) => Field(item, name) is true;
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static ApiException GraphError(string detail) => new(502, detail);

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = response.StatusCode == HttpStatusCode.Unauthorized
            ? "Microsoft identity authentication failed" : "Microsoft Graph request failed";
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = body.RootElement;
            var message = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? GetString(error, "message") : GetString(root, "error_description");
            return $"{message ?? fallback} ({(int)response.StatusCode})";
        }
        catch (JsonException) { return $"{fallback} ({(int)response.StatusCode})"; }
    }
}
