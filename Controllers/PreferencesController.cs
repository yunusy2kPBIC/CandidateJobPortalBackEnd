using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize, Route("api/preferences")]
public sealed class PreferencesController(PortalDbContext database) : PortalControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PreferenceResponse>> Get(CancellationToken cancellationToken)
    {
        var preferences = await GetOrCreateAsync(cancellationToken);
        return preferences.ToResponse();
    }

    [HttpPut]
    public async Task<ActionResult<PreferenceResponse>> Update(
        PreferenceUpdateRequest payload, CancellationToken cancellationToken)
    {
        if (!PortalValues.Languages.Contains(payload.Language))
            throw new ApiException(400, "language must be English or Arabic");
        if (!PortalValues.Themes.Contains(payload.Theme))
            throw new ApiException(400, "theme must be light or dark");

        var preferences = await GetOrCreateAsync(cancellationToken);
        preferences.EmailUpdates = payload.EmailUpdates;
        preferences.JobAlerts = payload.JobAlerts;
        preferences.Marketing = payload.Marketing;
        preferences.Language = payload.Language;
        preferences.Theme = payload.Theme;
        preferences.UpdatedAt = PortalClock.UtcNow();
        await database.SaveChangesAsync(cancellationToken);
        return preferences.ToResponse();
    }

    private async Task<UserPreference> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var existing = await database.UserPreferences.FindAsync([CurrentUserId], cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var userExists = await database.Users.AnyAsync(value => value.Id == CurrentUserId, cancellationToken);
        if (!userExists)
            throw new ApiException(401, "Invalid or expired token");
        var preferences = new UserPreference { UserId = CurrentUserId };
        database.UserPreferences.Add(preferences);
        await database.SaveChangesAsync(cancellationToken);
        return preferences;
    }
}
