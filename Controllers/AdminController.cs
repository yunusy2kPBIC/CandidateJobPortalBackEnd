using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize(Roles = PortalRoles.RecruitmentAdministrators), Route("api/admin")]
public sealed class AdminController(
    PortalDbContext database,
    PortalOptions options,
    SharePointOutboxService sharePointOutbox,
    AuditLogService auditLogs,
    MasterDataService masterData) : PortalControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<AdminSummaryResponse>> Summary(CancellationToken cancellationToken)
    {
        var users = await database.Users.CountAsync(cancellationToken);
        var candidates = await database.Users.CountAsync(value => value.Role == PortalRoles.Candidate, cancellationToken);
        var admins = await database.Users.CountAsync(value => value.Role == PortalRoles.Administrator, cancellationToken);
        var today = PortalClock.UtcNow().Date;
        var tomorrow = today.AddDays(1);
        var openJobs = await database.Jobs.CountAsync(
            value => value.IsPublished && value.IsOpen && value.PostedAt < tomorrow &&
                (value.ExpiresAt == null || value.ExpiresAt >= today), cancellationToken);
        var applications = await database.Applications.CountAsync(cancellationToken);
        return new AdminSummaryResponse(users, candidates, admins, openJobs, applications);
    }

    [Authorize(Roles = PortalRoles.Administrator)]
    [HttpGet("audit-logs")]
    public async Task<ActionResult<IReadOnlyList<AdminAuditLogResponse>>> AuditLogs(
        CancellationToken cancellationToken)
    {
        var entries = await database.AuditLogs.AsNoTracking()
            .Include(value => value.AdminUser)
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Take(250)
            .ToListAsync(cancellationToken);
        return entries.Select(value => new AdminAuditLogResponse(
            value.Id,
            value.Action,
            value.EntityType,
            value.EntityId,
            value.Details,
            value.CreatedAt,
            value.AdminUserId,
            value.AdminUser.FullName,
            value.AdminUser.Email)).ToArray();
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<JobResponse>>> Jobs(CancellationToken cancellationToken)
    {
        var jobs = await database.Jobs.AsNoTracking()
            .OrderByDescending(value => value.PostedAt)
            .ThenByDescending(value => value.Id)
            .ToListAsync(cancellationToken);
        return jobs.Select(value => value.ToResponse()).ToArray();
    }

    [HttpGet("job-options")]
    public async Task<ActionResult<AdminJobOptionsResponse>> JobOptions(CancellationToken cancellationToken)
    {
        var lookup = await masterData.GetOptionsAsync(cancellationToken);
        var citiesByCountry = lookup.Countries.ToDictionary(
            value => value.Name, value => value.Cities, StringComparer.OrdinalIgnoreCase);
        return new AdminJobOptionsResponse(
            lookup.Countries.Select(value => value.Name).ToArray(),
            lookup.Countries.SelectMany(value => value.Cities).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            citiesByCountry,
            lookup.Nationalities,
            lookup.Divisions,
            lookup.JobFunctions,
            lookup.CareerLevels);
    }

    [HttpPost("jobs")]
    public async Task<ActionResult<JobResponse>> CreateJob(
        AdminJobCreateRequest payload, CancellationToken cancellationToken)
    {
        ValidateEmploymentType(payload.EmploymentType);
        await masterData.ValidateJobAsync(payload.Country, payload.City, payload.Division,
            payload.JobFunction, payload.CareerLevel, cancellationToken);
        var postedAt = payload.PostedAt?.Date ?? PortalClock.UtcNow().Date;
        ValidatePostingDate(postedAt);
        var expiresAt = payload.ExpiresAt?.Date
            ?? throw new ApiException(400, "expires_at is required");
        ValidateExpiryDate(expiresAt, postedAt);
        var job = new Job
        {
            Title = payload.Title.Trim(),
            Division = payload.Division.Trim(),
            Country = payload.Country.Trim(),
            City = payload.City.Trim(),
            JobFunction = payload.JobFunction.Trim(),
            CareerLevel = payload.CareerLevel,
            EmploymentType = payload.EmploymentType,
            Summary = payload.Summary.Trim(),
            Description = payload.Description.Trim(),
            Requirements = payload.Requirements.Trim(),
            IsOpen = payload.IsOpen,
            IsPublished = payload.IsPublished,
            IsFeatured = payload.IsFeatured,
            PostedAt = postedAt,
            ExpiresAt = expiresAt,
        };
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Jobs.Add(job);
        await database.SaveChangesAsync(cancellationToken);
        sharePointOutbox.EnqueueJob(job.Id);
        auditLogs.Add(CurrentUserId, "Created", "Job posting", job.Id.ToString(),
            $"Created job “{job.Title}” with expiry date {job.ExpiresAt:yyyy-MM-dd}.");
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, job.ToResponse());
    }

    [HttpPatch("jobs/{jobId:int}")]
    public async Task<ActionResult<JobResponse>> UpdateJob(
        int jobId, AdminJobUpdateRequest payload, CancellationToken cancellationToken)
    {
        if (payload.EmploymentType is not null && !PortalValues.EmploymentTypes.Contains(payload.EmploymentType))
            throw new ApiException(400, "employment_type is invalid");
        var job = await database.Jobs.FindAsync([jobId], cancellationToken)
            ?? throw new ApiException(404, "Job not found");
        await masterData.ValidateJobAsync(
            payload.Country ?? job.Country,
            payload.City ?? job.City,
            payload.Division ?? job.Division,
            payload.JobFunction ?? job.JobFunction,
            payload.CareerLevel ?? job.CareerLevel,
            cancellationToken);
        var postedAt = payload.PostedAt?.Date ?? job.PostedAt.Date;
        var expiresAt = payload.ExpiresAt?.Date ?? job.ExpiresAt?.Date;
        if (payload.PostedAt is not null && payload.PostedAt.Value.Date != job.PostedAt.Date)
            ValidatePostingDate(postedAt);
        if (expiresAt is not null) ValidateExpiryDate(expiresAt.Value, postedAt);
        var changedFields = JobChangedFields(job, payload);

        if (payload.Title is not null) job.Title = payload.Title.Trim();
        if (payload.Division is not null) job.Division = payload.Division.Trim();
        if (payload.Country is not null) job.Country = payload.Country.Trim();
        if (payload.City is not null) job.City = payload.City.Trim();
        if (payload.JobFunction is not null) job.JobFunction = payload.JobFunction.Trim();
        if (payload.CareerLevel is not null) job.CareerLevel = payload.CareerLevel;
        if (payload.EmploymentType is not null) job.EmploymentType = payload.EmploymentType;
        if (payload.Summary is not null) job.Summary = payload.Summary.Trim();
        if (payload.Description is not null) job.Description = payload.Description.Trim();
        if (payload.Requirements is not null) job.Requirements = payload.Requirements.Trim();
        if (payload.IsOpen is not null) job.IsOpen = payload.IsOpen.Value;
        if (payload.IsPublished is not null) job.IsPublished = payload.IsPublished.Value;
        if (payload.IsFeatured is not null) job.IsFeatured = payload.IsFeatured.Value;
        if (payload.PostedAt is not null) job.PostedAt = payload.PostedAt.Value.Date;
        if (payload.ExpiresAt is not null) job.ExpiresAt = payload.ExpiresAt.Value.Date;

        if (changedFields.Count > 0)
        {
            auditLogs.Add(CurrentUserId, "Updated", "Job posting", job.Id.ToString(),
                $"Updated job “{job.Title}”: {string.Join(", ", changedFields)}.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        sharePointOutbox.EnqueueJob(job.Id);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job.ToResponse();
    }

    [Authorize(Roles = PortalRoles.Administrator)]
    [HttpDelete("jobs/{jobId:int}")]
    public async Task<ActionResult<MessageResponse>> DeleteJob(int jobId, CancellationToken cancellationToken)
    {
        var job = await database.Jobs.FindAsync([jobId], cancellationToken)
            ?? throw new ApiException(404, "Job not found");
        if (await database.Applications.AnyAsync(value => value.JobId == job.Id, cancellationToken))
            throw new ApiException(409, "This job has candidate applications and cannot be deleted. Close it instead.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        sharePointOutbox.EnqueueJobDelete(job.Id);
        auditLogs.Add(CurrentUserId, "Deleted", "Job posting", job.Id.ToString(),
            $"Deleted job “{job.Title}”.");
        database.Jobs.Remove(job);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MessageResponse("Job posting deleted successfully");
    }

    [HttpGet("candidates")]
    public async Task<ActionResult<IReadOnlyList<AdminCandidateResponse>>> Candidates(
        CancellationToken cancellationToken)
    {
        var rows = await database.Users.AsNoTracking()
            .Where(value => value.Role == PortalRoles.Candidate)
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Select(value => new { User = value, ApplicationCount = value.Applications.Count })
            .ToListAsync(cancellationToken);
        return rows.Select(value => CandidateResponse(value.User, value.ApplicationCount)).ToArray();
    }

    [HttpGet("candidates/{candidateId:int}/resume")]
    public async Task<IActionResult> CandidateResume(int candidateId, CancellationToken cancellationToken)
    {
        var candidate = await database.Users.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == candidateId, cancellationToken);
        if (candidate is null || candidate.Role != PortalRoles.Candidate || string.IsNullOrWhiteSpace(candidate.ResumePath))
            throw new ApiException(404, "Candidate CV not found");
        if (candidate.ResumePath.StartsWith("sharepoint-pending", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(409, "Candidate CV is queued for SharePoint upload");
        if (Uri.TryCreate(candidate.ResumePath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return Redirect(candidate.ResumePath);
        if (candidate.ResumePath.StartsWith("sharepoint-item:", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(404, "Candidate CV link is unavailable");

        var resumeRoot = Path.GetFullPath(Path.Combine(
            Path.GetFullPath(options.LocalStoragePath, AppContext.BaseDirectory), "resumes"));
        var resumePath = Path.GetFullPath(candidate.ResumePath);
        var relativePath = Path.GetRelativePath(resumeRoot, resumePath);
        if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}") ||
            Path.IsPathRooted(relativePath) || !System.IO.File.Exists(resumePath))
            throw new ApiException(404, "Candidate CV not found");
        return PhysicalFile(resumePath, ContentType(candidate.ResumeName ?? resumePath),
            candidate.ResumeName ?? Path.GetFileName(resumePath), enableRangeProcessing: true);
    }

    [HttpGet("applications")]
    public async Task<ActionResult<IReadOnlyList<AdminApplicationResponse>>> Applications(
        [FromQuery(Name = "status")] string status = "",
        CancellationToken cancellationToken = default)
    {
        var query = database.Applications.AsNoTracking()
            .Include(value => value.Job)
            .Include(value => value.User)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(value => value.Status == status.Trim());
        var applications = await query.OrderByDescending(value => value.AppliedAt)
            .ThenByDescending(value => value.Id)
            .ToListAsync(cancellationToken);
        return applications.Select(ApplicationResponse).ToArray();
    }

    [HttpPatch("applications/{applicationId:int}/status")]
    public async Task<ActionResult<AdminApplicationResponse>> UpdateApplicationStatus(
        int applicationId, ApplicationStatusUpdateRequest payload, CancellationToken cancellationToken)
    {
        if (!PortalValues.ApplicationStatuses.Contains(payload.Status))
            throw new ApiException(400, "status is invalid");
        var application = await database.Applications
            .Include(value => value.Job)
            .Include(value => value.User).ThenInclude(value => value.Applications)
            .SingleOrDefaultAsync(value => value.Id == applicationId, cancellationToken)
            ?? throw new ApiException(404, "Application not found");

        var hiredApplication = application.User.Applications.FirstOrDefault(value =>
            value.Id != application.Id && value.Status == "Hired");
        if (hiredApplication is not null)
            throw new ApiException(409,
                $"This application is disabled because the candidate was hired under APP-{hiredApplication.Id:0000}");

        if (application.Status != payload.Status)
        {
            var previousStatus = application.Status;
            application.Status = payload.Status;
            database.Notifications.Add(new Notification
            {
                UserId = application.UserId,
                Kind = payload.Status == "Interview" ? "interview" : "application",
                Title = "Application status updated",
                Message = $"Your application for {application.Job.Title} is now {payload.Status}.",
                Link = "/applications",
            });
            auditLogs.Add(CurrentUserId, "Status changed", "Application", application.Id.ToString(),
                $"Changed APP-{application.Id:0000} for “{application.Job.Title}” from {previousStatus} to {payload.Status}.");
        }
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        sharePointOutbox.EnqueueApplication(application.Id);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ApplicationResponse(application);
    }

    private AdminCandidateResponse CandidateResponse(User user, int applicationCount) => new(
        user.Id, user.Email, user.FirstName, user.LastName, user.Phone, user.Country, user.Nationality, user.Gender,
        user.City, user.Title, user.ResumeName, CandidateResumeUrl(user), applicationCount, user.CreatedAt);

    private string? CandidateResumeUrl(User user)
    {
        if (string.IsNullOrWhiteSpace(user.ResumePath)) return null;
        if (user.ResumePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            user.ResumePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return user.ResumePath;
        return user.ResumePath.StartsWith("sharepoint-item:", StringComparison.OrdinalIgnoreCase)
            || user.ResumePath.StartsWith("sharepoint-pending", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"/api/admin/candidates/{user.Id}/resume";
    }

    private AdminApplicationResponse ApplicationResponse(Application application) => new(
        application.Id, $"APP-{application.Id:0000}", application.Status, application.AppliedAt,
        CandidateResponse(application.User, application.User.Applications.Count), application.Job.ToResponse());

    private static void ValidateEmploymentType(string employmentType)
    {
        if (!PortalValues.EmploymentTypes.Contains(employmentType))
            throw new ApiException(400, "employment_type is invalid");
    }

    private static void ValidateExpiryDate(DateTime expiresAt, DateTime postedAt)
    {
        if (expiresAt.Date < postedAt.Date)
            throw new ApiException(400, "expires_at cannot be earlier than the posting date");
    }

    private static void ValidatePostingDate(DateTime postedAt)
    {
        if (postedAt.Date < PortalClock.UtcNow().Date)
            throw new ApiException(400, "posted_at must be today or a future date");
    }

    private static List<string> JobChangedFields(Job job, AdminJobUpdateRequest payload)
    {
        var changes = new List<string>();
        if (payload.Title is not null && job.Title != payload.Title.Trim()) changes.Add("title");
        if (payload.Division is not null && job.Division != payload.Division.Trim()) changes.Add("division");
        if (payload.Country is not null && job.Country != payload.Country.Trim()) changes.Add("country");
        if (payload.City is not null && job.City != payload.City.Trim()) changes.Add("city");
        if (payload.JobFunction is not null && job.JobFunction != payload.JobFunction.Trim()) changes.Add("job function");
        if (payload.CareerLevel is not null && job.CareerLevel != payload.CareerLevel) changes.Add("career level");
        if (payload.EmploymentType is not null && job.EmploymentType != payload.EmploymentType) changes.Add("employment type");
        if (payload.Summary is not null && job.Summary != payload.Summary.Trim()) changes.Add("summary");
        if (payload.Description is not null && job.Description != payload.Description.Trim()) changes.Add("description");
        if (payload.Requirements is not null && job.Requirements != payload.Requirements.Trim()) changes.Add("requirements");
        if (payload.IsOpen is not null && job.IsOpen != payload.IsOpen.Value) changes.Add("open status");
        if (payload.IsPublished is not null && job.IsPublished != payload.IsPublished.Value) changes.Add("publish status");
        if (payload.IsFeatured is not null && job.IsFeatured != payload.IsFeatured.Value) changes.Add("featured status");
        if (payload.PostedAt is not null && job.PostedAt.Date != payload.PostedAt.Value.Date) changes.Add("posting date");
        if (payload.ExpiresAt is not null && job.ExpiresAt?.Date != payload.ExpiresAt.Value.Date) changes.Add("expiry date");
        return changes;
    }

    private static string ContentType(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
    };
}
