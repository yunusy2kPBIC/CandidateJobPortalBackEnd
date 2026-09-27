using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize, Route("api/profile")]
public sealed class ProfileController(
    PortalDbContext database,
    SharePointOutboxService sharePointOutbox,
    DocumentStorage storage,
    MasterDataService masterData) : PortalControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserResponse>> Get(CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        return user.ToResponse();
    }

    [HttpPut]
    public async Task<ActionResult<UserResponse>> Update(ProfileUpdateRequest payload, CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        var nationality = payload.Nationality.Trim();
        if (user.Role == PortalRoles.Candidate)
            await masterData.ValidateNationalityAsync(nationality, cancellationToken);
        var gender = payload.Gender.Trim();
        if (user.Role is PortalRoles.Candidate or PortalRoles.Student && !PortalValues.Genders.Contains(gender))
            throw new ApiException(400, "Select a valid gender");
        await masterData.ValidateResidenceCountryAsync(payload.Country, cancellationToken);
        user.FirstName = payload.FirstName.Trim(); user.LastName = payload.LastName.Trim();
        user.CountryCode = payload.CountryCode.Trim(); user.Phone = payload.Phone.Trim(); user.Country = payload.Country.Trim();
        user.Nationality = nationality; user.Gender = gender; user.City = payload.City.Trim(); user.Title = payload.Title.Trim(); user.About = payload.About.Trim();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        sharePointOutbox.EnqueueCandidate(user.Id);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.ToResponse();
    }

    [HttpPost("resume")]
    [RequestSizeLimit(5 * 1024 * 1024 + 1024 * 64)]
    public async Task<ActionResult<MessageResponse>> UploadResume(IFormFile resume, CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        if (user.Role != PortalRoles.Candidate) throw new ApiException(403, "Only candidate accounts can upload resumes");
        var extension = Path.GetExtension(resume.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".doc" or ".docx")) throw new ApiException(400, "Upload a PDF, DOC or DOCX resume");
        if (resume.Length > 5 * 1024 * 1024) throw new ApiException(400, "Resume must be smaller than 5 MB");

        if (sharePointOutbox.Enabled)
        {
            await using var memory = new MemoryStream();
            await resume.CopyToAsync(memory, cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var queued = sharePointOutbox.EnqueueResume(user.Id, resume.FileName,
                resume.ContentType ?? "application/octet-stream", memory.ToArray())
                ?? throw new ApiException(503, "SharePoint resume queue is unavailable");
            user.ResumeName = resume.FileName;
            user.ResumePath = "sharepoint-pending";
            await database.SaveChangesAsync(cancellationToken);
            user.ResumePath = SharePointOutboxWorker.PendingResumePath(queued.Id);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MessageResponse("Resume saved and queued for SharePoint upload");
        }

        var savedPath = await storage.SaveResumeAsync(user.Id, resume, cancellationToken);
        user.ResumeName = resume.FileName;
        user.ResumePath = savedPath;
        await database.SaveChangesAsync(cancellationToken);
        return new MessageResponse("Resume uploaded to document storage successfully");
    }
}
