using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Route("api/privacy")]
public sealed class PrivacyController(
    PortalDbContext database,
    PrivacyNoticeService privacyNotice) : PortalControllerBase
{
    [AllowAnonymous, HttpGet]
    public ActionResult<PrivacyNoticeResponse> Notice() => privacyNotice.Current();

    [Authorize, HttpGet("consent")]
    public async Task<ActionResult<ConsentEvidenceResponse>> Consent(CancellationToken cancellationToken)
    {
        var consent = await database.UserConsents.AsNoTracking()
            .Where(value => value.UserId == CurrentUserId && value.DocumentType == "privacy_notice")
            .OrderByDescending(value => value.AcceptedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ApiException(404, "Privacy consent evidence was not found");
        return new ConsentEvidenceResponse(
            consent.DocumentType,
            consent.DocumentVersion,
            consent.AcceptedAt,
            consent.IpAddress,
            consent.UserAgent);
    }
}
