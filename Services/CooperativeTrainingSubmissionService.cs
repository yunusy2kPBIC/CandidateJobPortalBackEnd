using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Infrastructure;

namespace CandidatePortal.Api.Services;

public sealed class CooperativeTrainingSubmissionService(
    ISharePointClient client,
    PortalOptions options,
    AuditLogService auditLogs)
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public CooperativeTrainingCreateRequest ParsePayload(string payload)
    {
        CooperativeTrainingCreateRequest request;
        try
        {
            request = JsonSerializer.Deserialize<CooperativeTrainingCreateRequest>(payload, PayloadOptions)
                ?? throw new JsonException("Request payload is empty");
        }
        catch (JsonException error)
        {
            throw new ApiException(422, $"Invalid cooperative training payload: {error.Message}", error);
        }

        ValidateObject(request);
        return request;
    }

    public async Task<CooperativeTrainingResponse?> FindLatestByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var items = await client.ListItemsAsync(options.SharePointCooperativeTrainingList, cancellationToken);
        return items
            .Select(CooperativeTrainingResponse.FromItem)
            .Where(value => string.Equals(value.Email, email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => int.TryParse(value.Id, out var id) ? id : 0)
            .FirstOrDefault();
    }

    public async Task<CooperativeTrainingResponse> CreateAsync(
        CooperativeTrainingCreateRequest request,
        IFormFile transcript,
        IFormFile universityRequest,
        int actorUserId,
        string? ownerEmail = null,
        string? ownerFirstName = null,
        string? ownerLastName = null,
        CancellationToken cancellationToken = default)
    {
        var transcriptContent = await ReadDocumentAsync(transcript, cancellationToken);
        var universityRequestContent = await ReadDocumentAsync(universityRequest, cancellationToken);
        var applicantEmail = (ownerEmail ?? request.Email).Trim().ToLowerInvariant();
        var firstName = (ownerFirstName ?? request.FirstName).Trim();
        var lastName = (ownerLastName ?? request.LastName).Trim();
        var fields = request.ToFields();
        fields["Title"] = $"{firstName} {lastName}".Trim();
        fields["FirstName"] = firstName;
        fields["LastName"] = lastName;
        fields["Email"] = applicantEmail;

        SharePointItemResponse? created = null;
        var uploadedDocumentIds = new List<int>();
        try
        {
            created = await client.CreateItemAsync(
                options.SharePointCooperativeTrainingList, fields, cancellationToken);
            var requestId = int.Parse(created.Id);
            var uploadedTranscript = await client.UploadCooperativeTrainingDocumentAsync(
                requestId, applicantEmail, "Transcript", transcript.FileName, transcriptContent,
                transcript.ContentType ?? "application/octet-stream", cancellationToken);
            uploadedDocumentIds.Add(int.Parse(uploadedTranscript.Id));
            var uploadedUniversityRequest = await client.UploadCooperativeTrainingDocumentAsync(
                requestId, applicantEmail, "University Request", universityRequest.FileName,
                universityRequestContent, universityRequest.ContentType ?? "application/octet-stream", cancellationToken);
            uploadedDocumentIds.Add(int.Parse(uploadedUniversityRequest.Id));
            var updated = await client.UpdateItemAsync(options.SharePointCooperativeTrainingList, requestId,
                new Dictionary<string, object?>
                {
                    ["TranscriptUrl"] = uploadedTranscript.WebUrl,
                    ["TranscriptFileName"] = transcript.FileName,
                    ["UniversityRequestUrl"] = uploadedUniversityRequest.WebUrl,
                    ["UniversityRequestFileName"] = universityRequest.FileName,
                }, cancellationToken);
            await auditLogs.RecordAsync(actorUserId, "Created", "Cooperative training request", requestId.ToString(),
                $"Created cooperative training request for {firstName} {lastName}.", cancellationToken);
            return CooperativeTrainingResponse.FromItem(updated);
        }
        catch
        {
            foreach (var documentId in uploadedDocumentIds)
            {
                try { await client.DeleteItemAsync(options.SharePointCooperativeTrainingDocumentsLibrary, documentId, cancellationToken); }
                catch { /* Preserve the original failure. */ }
            }
            if (created is not null && int.TryParse(created.Id, out var createdId))
            {
                try { await client.DeleteItemAsync(options.SharePointCooperativeTrainingList, createdId, cancellationToken); }
                catch { /* Preserve the original failure. */ }
            }
            throw;
        }
    }

    private static async Task<byte[]> ReadDocumentAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".doc" or ".docx"))
            throw new ApiException(400, "Upload PDF, DOC, or DOCX documents");
        if (upload.Length == 0)
            throw new ApiException(400, "Uploaded documents cannot be empty");
        if (upload.Length > 10 * 1024L * 1024L)
            throw new ApiException(400, "Each document must be smaller than 10 MB");
        await using var stream = new MemoryStream();
        await upload.CopyToAsync(stream, cancellationToken);
        return stream.ToArray();
    }

    private static void ValidateObject(object value)
    {
        var failures = new List<ValidationResult>();
        if (Validator.TryValidateObject(value, new ValidationContext(value), failures, validateAllProperties: true))
            return;
        throw new ApiException(422, string.Join("; ", failures.Select(result => result.ErrorMessage)));
    }
}
