using System.Net;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CandidatePortal.Api.Controllers;

[Route("api/development/email")]
public sealed class DevelopmentEmailController(
    IHostEnvironment environment,
    PortalOptions options,
    IEmailSender emailSender) : PortalControllerBase
{
    [AllowAnonymous, HttpPost("test"), EnableRateLimiting("password-recovery")]
    public async Task<ActionResult<EmailTestResponse>> SendTestEmail(
        EmailTestRequest payload,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return NotFound();

        var remoteAddress = HttpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
            throw new ApiException(StatusCodes.Status403Forbidden, "The email test endpoint is restricted to localhost");

        if (!options.EmailEnabled)
            throw new ApiException(StatusCodes.Status409Conflict, "Set EMAIL_ENABLED=Y and restart the API before testing email delivery");

        var recipientAddress = payload.RecipientAddress.Trim().ToLowerInvariant();
        var submittedAt = DateTime.UtcNow;
        var subject = string.IsNullOrWhiteSpace(payload.Subject)
            ? "PBICareerPosting SMTP test"
            : payload.Subject.Trim();
        var message = string.IsNullOrWhiteSpace(payload.Message)
            ? $"This is a PBICareerPosting SMTP test sent at {submittedAt:O} UTC."
            : payload.Message.Trim();

        await emailSender.SendAsync(recipientAddress, subject, message, cancellationToken);

        return new EmailTestResponse(
            "Test email accepted for delivery.",
            recipientAddress,
            submittedAt);
    }
}
