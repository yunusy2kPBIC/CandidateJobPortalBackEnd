using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize, Route("api/dashboard")]
public sealed class DashboardController(PortalDbContext database) : PortalControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == CurrentUserId, cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        var applications = await database.Applications.CountAsync(
            value => value.UserId == user.Id, cancellationToken);
        var interviews = await database.Applications.CountAsync(
            value => value.UserId == user.Id &&
                (value.Status == "Interview" || value.Status == "Shortlisted"), cancellationToken);
        var today = PortalClock.UtcNow().Date;
        var openJobs = await database.Jobs.CountAsync(
            value => value.IsOpen && (value.ExpiresAt == null || value.ExpiresAt >= today), cancellationToken);
        var profileFields = new[]
        {
            user.FirstName, user.LastName, user.Email, user.Phone, user.Country,
            user.City, user.Title, user.About, user.ResumeName ?? "",
        };
        var profileComplete = (int)Math.Round(
            profileFields.Count(value => !string.IsNullOrWhiteSpace(value)) / (double)profileFields.Length * 100);

        var recent = await database.Applications.AsNoTracking()
            .Where(value => value.UserId == user.Id)
            .OrderByDescending(value => value.AppliedAt)
            .Select(value => new { value.AppliedAt, value.Job.Title })
            .Take(3)
            .ToListAsync(cancellationToken);
        var activity = recent
            .Select(value => new DashboardActivity($"Application submitted for {value.Title}", value.AppliedAt))
            .ToList();
        if (!string.IsNullOrWhiteSpace(user.ResumeName))
        {
            activity.Add(new DashboardActivity("Resume updated", user.CreatedAt));
        }

        return new DashboardResponse(applications, interviews, openJobs, profileComplete, activity);
    }
}
