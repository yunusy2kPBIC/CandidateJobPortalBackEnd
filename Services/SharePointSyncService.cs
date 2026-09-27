using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;

namespace CandidatePortal.Api.Services;

public sealed class SharePointSyncService(ISharePointClient client, PortalOptions options)
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool schemaVerified;

    public bool Enabled => options.SharePointSyncEnabled;

    public Task<SharePointItemResponse?> SyncCandidateAsync(User user, CancellationToken cancellationToken = default) =>
        UpsertAsync(options.SharePointCandidatesList, "PortalCandidateId", user.Id.ToString(), CandidateFields(user),
            "Email", user.Email, cancellationToken);

    public Task<SharePointItemResponse?> SyncJobAsync(Job job, CancellationToken cancellationToken = default) =>
        UpsertAsync(options.SharePointJobsList, "PortalJobId", job.Id.ToString(), JobFields(job), cancellationToken: cancellationToken);

    public async Task<SharePointItemResponse?> SyncApplicationAsync(Application application, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return null;
        var candidate = await SyncCandidateAsync(application.User, cancellationToken);
        var job = await SyncJobAsync(application.Job, cancellationToken);
        if (candidate is null || job is null) throw new ApiException(502, "Candidate and job must be synchronized before the application");
        return await UpsertAsync(options.SharePointApplicationsList, "PortalApplicationId", application.Id.ToString(),
            new Dictionary<string, object?>
            {
                ["PortalApplicationId"] = application.Id.ToString(),
                ["Title"] = $"APP-{application.Id:0000}",
                ["CandidateLookupId"] = candidate.Id,
                ["JobLookupId"] = job.Id,
                ["CandidateJobKey"] = $"{application.UserId}:{application.JobId}",
                ["Status"] = application.Status,
                ["AppliedAt"] = GraphDateTime(application.AppliedAt),
            }, cancellationToken: cancellationToken);
    }

    public async Task<bool> DeleteJobAsync(Job job, CancellationToken cancellationToken = default)
        => await DeleteJobByPortalIdAsync(job.Id, cancellationToken);

    public async Task<bool> DeleteJobByPortalIdAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return false;
        var existing = await client.FindItemByFieldAsync(options.SharePointJobsList, "PortalJobId", jobId.ToString(), cancellationToken);
        if (existing is null) return false;
        await client.DeleteItemAsync(options.SharePointJobsList, int.Parse(existing.Id), cancellationToken);
        return true;
    }

    public async Task<SharePointItemResponse> UploadCandidateResumeAsync(
        User user,
        string filename,
        byte[] content,
        string contentType,
        string? uploadKey = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var candidate = await SyncCandidateAsync(user, cancellationToken)
            ?? throw new ApiException(503, "SharePoint synchronization is disabled");
        var uploaded = await client.UploadResumeAsync(int.Parse(candidate.Id), user.Email,
            Path.GetFileName(filename), content, contentType, uploadKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(uploaded.WebUrl))
        {
            var resumeUrlField = await client.ResolveColumnNameAsync(
                options.SharePointCandidatesList, "ResumeUrl", cancellationToken);
            var resumeFields = new Dictionary<string, object?> { [resumeUrlField] = uploaded.WebUrl };
            try
            {
                await client.UpdateItemAsync(options.SharePointCandidatesList, int.Parse(candidate.Id),
                    resumeFields, cancellationToken);
            }
            catch (ApiException exception) when (IsUnknownField(exception))
            {
                await client.ProvisionAsync(cancellationToken);
                await client.UpdateItemAsync(options.SharePointCandidatesList, int.Parse(candidate.Id),
                    resumeFields, cancellationToken);
            }
        }
        return uploaded;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (schemaVerified) return;
        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (schemaVerified) return;
            await client.ProvisionAsync(cancellationToken);
            schemaVerified = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private async Task<SharePointItemResponse?> UpsertAsync(
        string listName, string keyField, string keyValue, IReadOnlyDictionary<string, object?> fields,
        string? fallbackField = null, string? fallbackValue = null, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return null;
        try
        {
            return await UpsertCoreAsync(listName, keyField, keyValue, fields,
                fallbackField, fallbackValue, cancellationToken);
        }
        catch (ApiException exception) when (IsUnknownField(exception))
        {
            await client.ProvisionAsync(cancellationToken);
            return await UpsertCoreAsync(listName, keyField, keyValue, fields,
                fallbackField, fallbackValue, cancellationToken);
        }
    }

    private static bool IsUnknownField(ApiException exception) =>
        exception.StatusCode == 502 &&
        exception.Detail.Contains("not recognized", StringComparison.OrdinalIgnoreCase);

    private async Task<SharePointItemResponse?> UpsertCoreAsync(
        string listName, string keyField, string keyValue, IReadOnlyDictionary<string, object?> fields,
        string? fallbackField, string? fallbackValue, CancellationToken cancellationToken)
    {
        var existing = await client.FindItemByFieldAsync(listName, keyField, keyValue, cancellationToken);
        if (existing is null && fallbackField is not null && fallbackValue is not null)
            existing = await client.FindItemByFieldAsync(listName, fallbackField, fallbackValue, cancellationToken);
        return existing is null
            ? await client.CreateItemAsync(listName, fields, cancellationToken)
            : await client.UpdateItemAsync(listName, int.Parse(existing.Id), fields, cancellationToken);
    }

    private static Dictionary<string, object?> CandidateFields(User user) => new()
    {
        ["PortalCandidateId"] = user.Id.ToString(),
        ["Title"] = user.FullName,
        ["Email"] = user.Email.ToLowerInvariant(),
        ["FirstName"] = user.FirstName.Trim(),
        ["LastName"] = user.LastName.Trim(),
        ["CountryCode"] = user.CountryCode.Trim(),
        ["Phone"] = user.Phone.Trim(),
        ["Country"] = user.Country.Trim(),
        ["Nationality"] = user.Nationality.Trim(),
        ["Gender"] = user.Gender.Trim(),
        ["City"] = user.City.Trim(),
        ["ProfessionalTitle"] = user.Title.Trim(),
        ["About"] = user.About.Trim(),
        ["Role"] = PortalRoles.SharePointName(user.Role),
    };

    private static Dictionary<string, object?> JobFields(Job job) => new()
    {
        ["PortalJobId"] = job.Id.ToString(),
        ["Title"] = job.Title.Trim(),
        ["Division"] = job.Division.Trim(),
        ["Country"] = job.Country.Trim(),
        ["City"] = job.City.Trim(),
        ["JobFunction"] = job.JobFunction.Trim(),
        ["CareerLevel"] = job.CareerLevel,
        ["EmploymentType"] = job.EmploymentType,
        ["Summary"] = job.Summary.Trim(),
        ["Description"] = job.Description.Trim(),
        ["Requirements"] = job.Requirements.Trim(),
        ["IsOpen"] = job.IsOpen,
        ["IsFeatured"] = job.IsFeatured,
        ["PostedAt"] = GraphDateTime(job.PostedAt),
        ["ExpiresAt"] = job.ExpiresAt is null ? null : GraphDateTime(job.ExpiresAt.Value),
    };

    internal static string GraphDateTime(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToString("O").Replace("+00:00", "Z");
}
