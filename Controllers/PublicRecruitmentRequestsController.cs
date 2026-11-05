using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CandidatePortal.Api.Controllers;

[AllowAnonymous]
[Route("api/public/recruitment-requests")]
public sealed class PublicRecruitmentRequestsController(
    ISharePointClient sharePoint,
    PortalOptions options,
    MasterDataService masterData,
    IEmailSender emailSender,
    ILogger<PublicRecruitmentRequestsController> logger) : PortalControllerBase
{
    [HttpPost]
    [EnableRateLimiting("public-recruitment-request")]
    public async Task<ActionResult<PublicRecruitmentRequestResponse>> Submit(
        PublicRecruitmentRequestCreate payload,
        CancellationToken cancellationToken)
    {
        await masterData.ValidateNationalityAsync(payload.Nationality, cancellationToken);

        var request = payload.ToInternalRequest();
        var created = await sharePoint.CreateItemAsync(
            options.SharePointRecruitmentRequestsList,
            request.ToFields(),
            cancellationToken);
        var reference = $"RR-{created.Id}";
        var confirmationEmailSent = options.EmailEnabled && options.SmtpConfigured;

        try
        {
            await emailSender.SendAsync(
                request.EmailAddress.Trim().ToLowerInvariant(),
                $"PBIC recruitment request received - {reference}",
                ConfirmationBody(request, reference),
                EmailPurposes.RecruitmentRequestConfirmation,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            confirmationEmailSent = false;
            logger.LogError(
                exception,
                "Recruitment request {Reference} was saved, but its confirmation email could not be sent",
                reference);
        }

        var message = confirmationEmailSent
            ? "Your recruitment request was submitted. A confirmation email has been sent."
            : "Your recruitment request was submitted, but the confirmation email could not be delivered.";
        return StatusCode(
            StatusCodes.Status201Created,
            new PublicRecruitmentRequestResponse(message, reference, confirmationEmailSent));
    }

    private static string ConfirmationBody(RecruitmentRequestCreate request, string reference) =>
        $"""
        Hello {request.Name.Trim()},

        Thank you for submitting your recruitment request to PBIC Career Job Posting.

        Reference: {reference}
        Preferred position: {request.PreferredPosition.Trim()}
        Email: {request.EmailAddress.Trim().ToLowerInvariant()}

        Our recruitment team will review your information and contact you if a suitable opportunity is available.

        Regards,
        PBIC Career Job Posting
        """;
}
