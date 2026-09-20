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

[Route("api")]
public sealed class JobsController(
    PortalDbContext database,
    SharePointSyncService sharePoint,
    MasterDataService masterData) : PortalControllerBase
{
    [AllowAnonymous, HttpGet("jobs")]
    public async Task<ActionResult<JobListResponse>> ListJobs(
        [FromQuery] string keywords = "", [FromQuery] string country = "", [FromQuery] string city = "",
        [FromQuery] string division = "", [FromQuery(Name = "job_function")] string jobFunction = "",
        [FromQuery(Name = "career_level")] string careerLevel = "", [FromQuery] string sort = "recent",
        CancellationToken cancellationToken = default)
    {
        if (sort is not ("recent" or "oldest" or "title"))
            throw new ApiException(422, "sort must be recent, oldest, or title");
        var today = PortalClock.UtcNow().Date;
        var tomorrow = today.AddDays(1);
        IQueryable<Job> query = database.Jobs.AsNoTracking()
            .Where(job => job.IsPublished && job.IsOpen && job.PostedAt < tomorrow &&
                (job.ExpiresAt == null || job.ExpiresAt >= today));
        if (!string.IsNullOrWhiteSpace(keywords))
        {
            var term = keywords.Trim().ToLower();
            query = query.Where(job =>
                job.Title.ToLower().Contains(term) || job.Summary.ToLower().Contains(term) || job.Description.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(country)) query = query.Where(job => job.Country == country.Trim());
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(job => job.City == city.Trim());
        if (!string.IsNullOrWhiteSpace(division)) query = query.Where(job => job.Division == division.Trim());
        if (!string.IsNullOrWhiteSpace(jobFunction)) query = query.Where(job => job.JobFunction == jobFunction.Trim());
        if (!string.IsNullOrWhiteSpace(careerLevel)) query = query.Where(job => job.CareerLevel == careerLevel.Trim());
        query = sort switch
        {
            "oldest" => query.OrderBy(job => job.PostedAt),
            "title" => query.OrderBy(job => job.Title),
            _ => query.OrderByDescending(job => job.PostedAt),
        };
        var jobs = await query.Select(job => job.ToResponse()).ToListAsync(cancellationToken);
        var options = await masterData.GetOptionsAsync(cancellationToken);
        var filters = new Dictionary<string, IReadOnlyList<string>>
        {
            ["countries"] = options.Countries.Select(value => value.Name).ToArray(),
            ["cities"] = options.Countries.SelectMany(value => value.Cities).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ["divisions"] = options.Divisions,
            ["job_functions"] = options.JobFunctions,
            ["career_levels"] = options.CareerLevels,
        };
        return new JobListResponse(jobs, jobs.Count, filters);
    }

    [AllowAnonymous, HttpGet("jobs/{jobId:int}")]
    public async Task<ActionResult<JobResponse>> GetJob(int jobId, CancellationToken cancellationToken)
    {
        var today = PortalClock.UtcNow().Date;
        var tomorrow = today.AddDays(1);
        var job = await database.Jobs.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == jobId && value.IsPublished && value.IsOpen && value.PostedAt < tomorrow &&
                    (value.ExpiresAt == null || value.ExpiresAt >= today),
                cancellationToken)
            ?? throw new ApiException(404, "Job not found");
        return job.ToResponse();
    }

    [Authorize(Roles = PortalRoles.Candidate), HttpPost("jobs/{jobId:int}/apply")]
    public async Task<ActionResult<MessageResponse>> Apply(int jobId, CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        if (user.Role != PortalRoles.Candidate) throw new ApiException(403, "Only candidate accounts can apply for jobs");
        try
        {
            await masterData.ValidateNationalityAsync(user.Nationality, cancellationToken);
        }
        catch (ApiException)
        {
            throw new ApiException(400, "Complete your profile and select your nationality before applying");
        }
        if (!PortalValues.Genders.Contains(user.Gender))
            throw new ApiException(400, "Complete your profile and select your gender before applying");
        if (string.IsNullOrWhiteSpace(user.ResumeName) || string.IsNullOrWhiteSpace(user.ResumePath))
            throw new ApiException(400, "Upload your resume before applying for a job");
        if (await database.Applications.AnyAsync(
                value => value.UserId == user.Id && value.Status == "Hired", cancellationToken))
            throw new ApiException(409, "You cannot submit another application after being hired");
        var job = await database.Jobs.FindAsync([jobId], cancellationToken);
        var today = PortalClock.UtcNow().Date;
        if (job is null || !job.IsPublished || !job.IsOpen || job.PostedAt.Date > today ||
            (job.ExpiresAt is not null && job.ExpiresAt.Value.Date < today))
            throw new ApiException(404, "This job is no longer available");
        if (await database.Applications.AnyAsync(value => value.UserId == user.Id && value.JobId == job.Id, cancellationToken))
            throw new ApiException(409, "You have already applied for this job");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var application = new Application { UserId = user.Id, JobId = job.Id, User = user, Job = job };
        database.Applications.Add(application);
        database.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Kind = "application",
            Title = "Application received",
            Message = $"We received your application for {job.Title}.",
            Link = "/applications",
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await sharePoint.SyncApplicationAsync(application, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException error)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ApiException(409, "You have already applied for this job", error);
        }
        return StatusCode(201, new MessageResponse("Application submitted successfully"));
    }

    [Authorize(Roles = PortalRoles.Candidate), HttpGet("applications")]
    public async Task<ActionResult<IReadOnlyList<ApplicationResponse>>> Applications(CancellationToken cancellationToken)
    {
        var applications = await database.Applications.AsNoTracking().Include(value => value.Job)
            .Where(value => value.UserId == CurrentUserId)
            .OrderByDescending(value => value.AppliedAt).ToListAsync(cancellationToken);
        return applications.Select(value => value.ToResponse()).ToArray();
    }
}
