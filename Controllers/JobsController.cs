using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Route("api")]
public sealed class JobsController(
    PortalDbContext database,
    SharePointSyncService sharePoint) : PortalControllerBase
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
        IQueryable<Job> query = database.Jobs.AsNoTracking()
            .Where(job => job.IsOpen && (job.ExpiresAt == null || job.ExpiresAt >= today));
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
        var openJobs = database.Jobs.AsNoTracking()
            .Where(job => job.IsOpen && (job.ExpiresAt == null || job.ExpiresAt >= today));
        var filters = new Dictionary<string, IReadOnlyList<string>>
        {
            ["countries"] = await openJobs.Select(job => job.Country).Distinct().Order().ToListAsync(cancellationToken),
            ["cities"] = await openJobs.Select(job => job.City).Distinct().Order().ToListAsync(cancellationToken),
            ["divisions"] = await openJobs.Select(job => job.Division).Distinct().Order().ToListAsync(cancellationToken),
            ["job_functions"] = await openJobs.Select(job => job.JobFunction).Distinct().Order().ToListAsync(cancellationToken),
            ["career_levels"] = await openJobs.Select(job => job.CareerLevel).Distinct().Order().ToListAsync(cancellationToken),
        };
        return new JobListResponse(jobs, jobs.Count, filters);
    }

    [AllowAnonymous, HttpGet("jobs/{jobId:int}")]
    public async Task<ActionResult<JobResponse>> GetJob(int jobId, CancellationToken cancellationToken)
    {
        var today = PortalClock.UtcNow().Date;
        var job = await database.Jobs.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == jobId && value.IsOpen && (value.ExpiresAt == null || value.ExpiresAt >= today),
                cancellationToken)
            ?? throw new ApiException(404, "Job not found");
        return job.ToResponse();
    }

    [Authorize, HttpPost("jobs/{jobId:int}/apply")]
    public async Task<ActionResult<MessageResponse>> Apply(int jobId, CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        if (user.Role != "candidate") throw new ApiException(403, "Only candidate accounts can apply for jobs");
        var job = await database.Jobs.FindAsync([jobId], cancellationToken);
        if (job is null || !job.IsOpen || (job.ExpiresAt is not null && job.ExpiresAt.Value.Date < PortalClock.UtcNow().Date))
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

    [Authorize, HttpGet("applications")]
    public async Task<ActionResult<IReadOnlyList<ApplicationResponse>>> Applications(CancellationToken cancellationToken)
    {
        var applications = await database.Applications.AsNoTracking().Include(value => value.Job)
            .Where(value => value.UserId == CurrentUserId)
            .OrderByDescending(value => value.AppliedAt).ToListAsync(cancellationToken);
        return applications.Select(value => value.ToResponse()).ToArray();
    }
}
