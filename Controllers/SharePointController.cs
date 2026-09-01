using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize(Roles = "admin"), Route("api/sharepoint")]
public sealed class SharePointController(
    ISharePointClient client,
    SharePointSyncService synchronization,
    PortalDbContext database,
    PortalOptions options,
    AuditLogService auditLogs) : PortalControllerBase
{
    [HttpGet("status")]
    public object Status() => client.ConfigurationStatus();

    [HttpGet("diagnostics")]
    public Task<object> Diagnostics(CancellationToken cancellationToken) =>
        client.DiagnosticsAsync(cancellationToken);

    [HttpGet("lists")]
    public Task<IReadOnlyList<SharePointListResponse>> Lists(CancellationToken cancellationToken) =>
        client.ListListsAsync(cancellationToken);

    [HttpPost("setup")]
    public async Task<SharePointSetupResponse> Setup(CancellationToken cancellationToken)
    {
        var result = await client.ProvisionAsync(cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Provisioned", "SharePoint", null,
            "Created or repaired the portal SharePoint resources.", cancellationToken);
        return result;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SharePointSyncResponse>> Sync(CancellationToken cancellationToken)
    {
        if (!synchronization.Enabled)
            throw new ApiException(409, "Portal SharePoint synchronization is disabled");

        await client.ProvisionAsync(cancellationToken);
        var candidates = await database.Users.Where(value => value.Role == "candidate")
            .OrderBy(value => value.Id).ToListAsync(cancellationToken);
        var jobs = await database.Jobs.OrderBy(value => value.Id).ToListAsync(cancellationToken);
        var applications = await database.Applications
            .Include(value => value.User).Include(value => value.Job)
            .OrderBy(value => value.Id).ToListAsync(cancellationToken);
        foreach (var candidate in candidates)
            await synchronization.SyncCandidateAsync(candidate, cancellationToken);
        foreach (var job in jobs)
            await synchronization.SyncJobAsync(job, cancellationToken);
        foreach (var application in applications)
            await synchronization.SyncApplicationAsync(application, cancellationToken);

        var uploadedResumes = 0;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.ResumePath) || IsHttpUrl(candidate.ResumePath) ||
                !System.IO.File.Exists(candidate.ResumePath))
                continue;
            var content = await System.IO.File.ReadAllBytesAsync(candidate.ResumePath, cancellationToken);
            var uploaded = await synchronization.UploadCandidateResumeAsync(
                candidate, candidate.ResumeName ?? Path.GetFileName(candidate.ResumePath), content,
                ContentType(candidate.ResumeName ?? candidate.ResumePath), cancellationToken);
            candidate.ResumePath = uploaded.WebUrl ?? $"sharepoint-item:{uploaded.Id}";
            uploadedResumes++;
        }
        await database.SaveChangesAsync(cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Synchronized", "SharePoint", null,
            $"Synchronized {candidates.Count} candidates, {jobs.Count} jobs, {applications.Count} applications, and {uploadedResumes} resumes.",
            cancellationToken);
        return new SharePointSyncResponse(
            "Portal content synchronized to SharePoint", candidates.Count, jobs.Count,
            applications.Count, uploadedResumes);
    }

    [HttpGet("candidates")]
    public Task<IReadOnlyList<SharePointItemResponse>> Candidates(CancellationToken cancellationToken) =>
        client.ListItemsAsync(options.SharePointCandidatesList, cancellationToken);

    [HttpPost("candidates")]
    public async Task<ActionResult<SharePointItemResponse>> CreateCandidate(
        SharePointCandidateCreateRequest payload, CancellationToken cancellationToken)
    {
        ValidateCandidateRole(payload.Role);
        var created = await client.CreateItemAsync(
            options.SharePointCandidatesList, payload.ToFields(), cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Created", "SharePoint candidate", created.Id,
            $"Created SharePoint candidate {payload.Email}.", cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("candidates/{itemId:int}")]
    public Task<SharePointItemResponse> Candidate(int itemId, CancellationToken cancellationToken) =>
        client.GetItemAsync(options.SharePointCandidatesList, itemId, cancellationToken);

    [HttpPatch("candidates/{itemId:int}")]
    public async Task<SharePointItemResponse> UpdateCandidate(
        int itemId, SharePointCandidateUpdateRequest payload, CancellationToken cancellationToken)
    {
        if (payload.Role is not null) ValidateCandidateRole(payload.Role);
        var updated = await client.UpdateItemAsync(options.SharePointCandidatesList, itemId, payload.ToFields(), cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Updated", "SharePoint candidate", itemId.ToString(),
            $"Updated SharePoint candidate item {itemId}.", cancellationToken);
        return updated;
    }

    [HttpDelete("candidates/{itemId:int}")]
    public Task<ActionResult<MessageResponse>> DeleteCandidate(int itemId, CancellationToken cancellationToken) =>
        DeleteItem(options.SharePointCandidatesList, itemId, "SharePoint candidate",
            "SharePoint item deleted successfully", cancellationToken);

    [HttpGet("jobs")]
    public Task<IReadOnlyList<SharePointItemResponse>> Jobs(CancellationToken cancellationToken) =>
        client.ListItemsAsync(options.SharePointJobsList, cancellationToken);

    [HttpPost("jobs")]
    public async Task<ActionResult<SharePointItemResponse>> CreateJob(
        SharePointJobCreateRequest payload, CancellationToken cancellationToken)
    {
        ValidateJobChoices(payload.CareerLevel, payload.EmploymentType);
        var created = await client.CreateItemAsync(options.SharePointJobsList, payload.ToFields(), cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Created", "SharePoint job", created.Id,
            $"Created SharePoint job “{payload.JobTitle}”.", cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("jobs/{itemId:int}")]
    public Task<SharePointItemResponse> Job(int itemId, CancellationToken cancellationToken) =>
        client.GetItemAsync(options.SharePointJobsList, itemId, cancellationToken);

    [HttpPatch("jobs/{itemId:int}")]
    public async Task<SharePointItemResponse> UpdateJob(
        int itemId, SharePointJobUpdateRequest payload, CancellationToken cancellationToken)
    {
        if (payload.CareerLevel is not null && !PortalValues.CareerLevels.Contains(payload.CareerLevel))
            throw new ApiException(400, "career_level is invalid");
        if (payload.EmploymentType is not null && !PortalValues.EmploymentTypes.Contains(payload.EmploymentType))
            throw new ApiException(400, "employment_type is invalid");
        var updated = await client.UpdateItemAsync(options.SharePointJobsList, itemId, payload.ToFields(), cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Updated", "SharePoint job", itemId.ToString(),
            $"Updated SharePoint job item {itemId}.", cancellationToken);
        return updated;
    }

    [HttpDelete("jobs/{itemId:int}")]
    public Task<ActionResult<MessageResponse>> DeleteJob(int itemId, CancellationToken cancellationToken) =>
        DeleteItem(options.SharePointJobsList, itemId, "SharePoint job",
            "SharePoint item deleted successfully", cancellationToken);

    [HttpGet("applications")]
    public Task<IReadOnlyList<SharePointItemResponse>> Applications(CancellationToken cancellationToken) =>
        client.ListItemsAsync(options.SharePointApplicationsList, cancellationToken);

    [HttpPost("applications")]
    public async Task<ActionResult<SharePointItemResponse>> CreateApplication(
        SharePointApplicationCreateRequest payload, CancellationToken cancellationToken)
    {
        ValidateApplicationStatus(payload.Status);
        var created = await client.CreateItemAsync(
            options.SharePointApplicationsList, payload.ToFields(), cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Created", "SharePoint application", created.Id,
            $"Created SharePoint application for candidate {payload.CandidateId} and job {payload.JobId}.", cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("applications/{itemId:int}")]
    public Task<SharePointItemResponse> Application(int itemId, CancellationToken cancellationToken) =>
        client.GetItemAsync(options.SharePointApplicationsList, itemId, cancellationToken);

    [HttpPatch("applications/{itemId:int}")]
    public async Task<SharePointItemResponse> UpdateApplication(
        int itemId, SharePointApplicationUpdateRequest payload, CancellationToken cancellationToken)
    {
        if (payload.Status is not null) ValidateApplicationStatus(payload.Status);
        var updated = await client.UpdateItemAsync(
            options.SharePointApplicationsList, itemId, payload.ToFields(), cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Updated", "SharePoint application", itemId.ToString(),
            $"Updated SharePoint application item {itemId}.", cancellationToken);
        return updated;
    }

    [HttpDelete("applications/{itemId:int}")]
    public Task<ActionResult<MessageResponse>> DeleteApplication(int itemId, CancellationToken cancellationToken) =>
        DeleteItem(options.SharePointApplicationsList, itemId, "SharePoint application",
            "SharePoint item deleted successfully", cancellationToken);

    [HttpGet("recruitment-requests")]
    public async Task<IReadOnlyList<RecruitmentRequestResponse>> RecruitmentRequests(
        CancellationToken cancellationToken)
    {
        var items = await client.ListItemsAsync(options.SharePointRecruitmentRequestsList, cancellationToken);
        return items.Select(RecruitmentRequestResponse.FromItem).ToArray();
    }

    [HttpPost("recruitment-requests")]
    public async Task<ActionResult<RecruitmentRequestResponse>> CreateRecruitmentRequest(
        RecruitmentRequestCreate payload, CancellationToken cancellationToken)
    {
        var item = await client.CreateItemAsync(
            options.SharePointRecruitmentRequestsList, payload.ToFields(), cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Created", "Recruitment request", item.Id,
            $"Created recruitment request for {payload.Name}.", cancellationToken);
        return StatusCode(StatusCodes.Status201Created, RecruitmentRequestResponse.FromItem(item));
    }

    [HttpGet("recruitment-requests/{itemId:int}")]
    public async Task<RecruitmentRequestResponse> RecruitmentRequest(
        int itemId, CancellationToken cancellationToken) =>
        RecruitmentRequestResponse.FromItem(await client.GetItemAsync(
            options.SharePointRecruitmentRequestsList, itemId, cancellationToken));

    [HttpPatch("recruitment-requests/{itemId:int}")]
    public async Task<RecruitmentRequestResponse> UpdateRecruitmentRequest(
        int itemId, RecruitmentRequestUpdate payload, CancellationToken cancellationToken)
    {
        var updated = RecruitmentRequestResponse.FromItem(await client.UpdateItemAsync(
            options.SharePointRecruitmentRequestsList, itemId, payload.ToFields(), cancellationToken));
        await auditLogs.RecordAsync(CurrentUserId, "Updated", "Recruitment request", itemId.ToString(),
            $"Updated recruitment request for {updated.Name}.", cancellationToken);
        return updated;
    }

    [HttpDelete("recruitment-requests/{itemId:int}")]
    public Task<ActionResult<MessageResponse>> DeleteRecruitmentRequest(
        int itemId, CancellationToken cancellationToken) =>
        DeleteItem(options.SharePointRecruitmentRequestsList, itemId, "Recruitment request",
            "SharePoint item deleted successfully", cancellationToken);

    [HttpGet("cooperative-training-requests")]
    public async Task<IReadOnlyList<CooperativeTrainingResponse>> CooperativeTrainingRequests(
        CancellationToken cancellationToken)
    {
        var items = await client.ListItemsAsync(options.SharePointCooperativeTrainingList, cancellationToken);
        return items.Select(CooperativeTrainingResponse.FromItem).ToArray();
    }

    [HttpPost("cooperative-training-requests")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<ActionResult<CooperativeTrainingResponse>> CreateCooperativeTrainingRequest(
        [FromForm] string payload,
        [FromForm] IFormFile transcript,
        [FromForm(Name = "university_request")] IFormFile universityRequest,
        CancellationToken cancellationToken)
    {
        CooperativeTrainingCreateRequest request;
        try
        {
            request = JsonSerializer.Deserialize<CooperativeTrainingCreateRequest>(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true,
            }) ?? throw new JsonException("Request payload is empty");
        }
        catch (JsonException error)
        {
            throw new ApiException(422, $"Invalid cooperative training payload: {error.Message}", error);
        }
        ValidateObject(request);
        var transcriptContent = await ReadTrainingDocumentAsync(transcript, cancellationToken);
        var universityRequestContent = await ReadTrainingDocumentAsync(universityRequest, cancellationToken);

        SharePointItemResponse? created = null;
        var uploadedDocumentIds = new List<int>();
        try
        {
            created = await client.CreateItemAsync(
                options.SharePointCooperativeTrainingList, request.ToFields(), cancellationToken);
            var requestId = int.Parse(created.Id);
            var uploadedTranscript = await client.UploadCooperativeTrainingDocumentAsync(
                requestId, request.Email, "Transcript", transcript.FileName, transcriptContent,
                transcript.ContentType ?? "application/octet-stream", cancellationToken);
            uploadedDocumentIds.Add(int.Parse(uploadedTranscript.Id));
            var uploadedUniversityRequest = await client.UploadCooperativeTrainingDocumentAsync(
                requestId, request.Email, "University Request", universityRequest.FileName,
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
            await auditLogs.RecordAsync(CurrentUserId, "Created", "Cooperative training request", requestId.ToString(),
                $"Created cooperative training request for {request.FirstName} {request.LastName}.", cancellationToken);
            return StatusCode(StatusCodes.Status201Created, CooperativeTrainingResponse.FromItem(updated));
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

    [HttpGet("cooperative-training-requests/{itemId:int}")]
    public async Task<CooperativeTrainingResponse> CooperativeTrainingRequest(
        int itemId, CancellationToken cancellationToken) =>
        CooperativeTrainingResponse.FromItem(await client.GetItemAsync(
            options.SharePointCooperativeTrainingList, itemId, cancellationToken));

    [HttpPatch("cooperative-training-requests/{itemId:int}")]
    public async Task<CooperativeTrainingResponse> UpdateCooperativeTrainingRequest(
        int itemId, CooperativeTrainingUpdateRequest payload, CancellationToken cancellationToken)
    {
        var existing = CooperativeTrainingResponse.FromItem(await client.GetItemAsync(
            options.SharePointCooperativeTrainingList, itemId, cancellationToken));
        var merged = payload.ApplyTo(existing);
        ValidateObject(merged);
        var updated = CooperativeTrainingResponse.FromItem(await client.UpdateItemAsync(
            options.SharePointCooperativeTrainingList, itemId, merged.ToFields(), cancellationToken));
        await auditLogs.RecordAsync(CurrentUserId, "Updated", "Cooperative training request", itemId.ToString(),
            $"Updated cooperative training request for {updated.FirstName} {updated.LastName}.", cancellationToken);
        return updated;
    }

    [HttpPost("cooperative-training-requests/{itemId:int}/documents")]
    [RequestSizeLimit(10 * 1024 * 1024 + 64 * 1024)]
    public async Task<CooperativeTrainingResponse> ReplaceCooperativeTrainingDocument(
        int itemId,
        [FromForm(Name = "document_type")] string documentType,
        [FromForm] IFormFile document,
        CancellationToken cancellationToken)
    {
        if (documentType is not ("Transcript" or "University Request"))
            throw new ApiException(400, "Select Transcript or University Request");
        var content = await ReadTrainingDocumentAsync(document, cancellationToken);
        var existing = CooperativeTrainingResponse.FromItem(await client.GetItemAsync(
            options.SharePointCooperativeTrainingList, itemId, cancellationToken));
        var uploaded = await client.UploadCooperativeTrainingDocumentAsync(
            itemId, existing.Email, documentType, document.FileName, content,
            document.ContentType ?? "application/octet-stream", cancellationToken);
        var fields = documentType == "Transcript"
            ? new Dictionary<string, object?>
            {
                ["TranscriptUrl"] = uploaded.WebUrl,
                ["TranscriptFileName"] = document.FileName,
            }
            : new Dictionary<string, object?>
            {
                ["UniversityRequestUrl"] = uploaded.WebUrl,
                ["UniversityRequestFileName"] = document.FileName,
            };
        var updated = CooperativeTrainingResponse.FromItem(await client.UpdateItemAsync(
            options.SharePointCooperativeTrainingList, itemId, fields, cancellationToken));
        await auditLogs.RecordAsync(CurrentUserId, "Replaced document", "Cooperative training request", itemId.ToString(),
            $"Replaced the {documentType.ToLowerInvariant()} for {updated.FirstName} {updated.LastName}.", cancellationToken);
        return updated;
    }

    [HttpDelete("cooperative-training-requests/{itemId:int}")]
    public async Task<ActionResult<MessageResponse>> DeleteCooperativeTrainingRequest(
        int itemId, CancellationToken cancellationToken)
    {
        await client.DeleteCooperativeTrainingDocumentsAsync(itemId, cancellationToken);
        await client.DeleteItemAsync(options.SharePointCooperativeTrainingList, itemId, cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Deleted", "Cooperative training request", itemId.ToString(),
            $"Deleted cooperative training request item {itemId} and its documents.", cancellationToken);
        return new MessageResponse("Cooperative training request deleted successfully");
    }

    [HttpGet("resumes")]
    public Task<IReadOnlyList<SharePointItemResponse>> Resumes(CancellationToken cancellationToken) =>
        client.ListItemsAsync(options.SharePointResumesLibrary, cancellationToken);

    [HttpPost("resumes")]
    [RequestSizeLimit(5 * 1024 * 1024 + 64 * 1024)]
    public async Task<ActionResult<SharePointItemResponse>> UploadResume(
        [FromForm(Name = "candidate_item_id")] int candidateItemId,
        [FromForm(Name = "candidate_email")] string candidateEmail,
        [FromForm] IFormFile resume,
        CancellationToken cancellationToken)
    {
        if (candidateItemId <= 0 || string.IsNullOrWhiteSpace(candidateEmail))
            throw new ApiException(400, "Candidate item and email are required");
        ValidateDocument(resume, 5, "Upload a PDF, DOC or DOCX resume", "Resume must be smaller than 5 MB");
        await using var stream = new MemoryStream();
        await resume.CopyToAsync(stream, cancellationToken);
        var uploaded = await client.UploadResumeAsync(
            candidateItemId, candidateEmail.Trim().ToLowerInvariant(), resume.FileName,
            stream.ToArray(), resume.ContentType ?? "application/octet-stream", cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Uploaded", "SharePoint resume", uploaded.Id,
            $"Uploaded a resume for {candidateEmail.Trim().ToLowerInvariant()}.", cancellationToken);
        return StatusCode(StatusCodes.Status201Created, uploaded);
    }

    [HttpDelete("resumes/{itemId:int}")]
    public Task<ActionResult<MessageResponse>> DeleteResume(int itemId, CancellationToken cancellationToken) =>
        DeleteItem(options.SharePointResumesLibrary, itemId, "SharePoint resume",
            "SharePoint item deleted successfully", cancellationToken);

    private async Task<ActionResult<MessageResponse>> DeleteItem(
        string listName, int itemId, string entityType, string message, CancellationToken cancellationToken)
    {
        await client.DeleteItemAsync(listName, itemId, cancellationToken);
        await auditLogs.RecordAsync(CurrentUserId, "Deleted", entityType, itemId.ToString(),
            $"Deleted {entityType.ToLowerInvariant()} item {itemId}.", cancellationToken);
        return new MessageResponse(message);
    }

    private static void ValidateCandidateRole(string role)
    {
        if (role is not ("Candidate" or "Admin"))
            throw new ApiException(400, "role must be Candidate or Admin");
    }

    private static void ValidateJobChoices(string careerLevel, string employmentType)
    {
        if (!PortalValues.CareerLevels.Contains(careerLevel))
            throw new ApiException(400, "career_level is invalid");
        if (!PortalValues.EmploymentTypes.Contains(employmentType))
            throw new ApiException(400, "employment_type is invalid");
    }

    private static void ValidateApplicationStatus(string status)
    {
        if (!PortalValues.ApplicationStatuses.Contains(status))
            throw new ApiException(400, "status is invalid");
    }

    private static void ValidateDocument(IFormFile upload, int maxMegabytes, string typeError, string sizeError)
    {
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".doc" or ".docx")) throw new ApiException(400, typeError);
        if (upload.Length > maxMegabytes * 1024L * 1024L) throw new ApiException(400, sizeError);
    }

    private static async Task<byte[]> ReadTrainingDocumentAsync(
        IFormFile upload, CancellationToken cancellationToken)
    {
        ValidateDocument(upload, 10, "Upload PDF, DOC, or DOCX documents",
            "Each document must be smaller than 10 MB");
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

    private static bool IsHttpUrl(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private static string ContentType(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
    };
}
