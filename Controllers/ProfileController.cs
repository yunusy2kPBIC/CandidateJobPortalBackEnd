using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize, Route("api/profile")]
public sealed class ProfileController(
    PortalDbContext database,
    SharePointSyncService sharePoint,
    DocumentStorage storage) : PortalControllerBase
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
        user.FirstName = payload.FirstName.Trim(); user.LastName = payload.LastName.Trim();
        user.CountryCode = payload.CountryCode.Trim(); user.Phone = payload.Phone.Trim(); user.Country = payload.Country.Trim();
        user.City = payload.City.Trim(); user.Title = payload.Title.Trim(); user.About = payload.About.Trim();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await sharePoint.SyncCandidateAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.ToResponse();
    }

    [HttpPost("resume")]
    [RequestSizeLimit(5 * 1024 * 1024 + 1024 * 64)]
    public async Task<ActionResult<MessageResponse>> UploadResume(IFormFile resume, CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        if (user.Role != "candidate") throw new ApiException(403, "Only candidate accounts can upload resumes");
        var extension = Path.GetExtension(resume.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".doc" or ".docx")) throw new ApiException(400, "Upload a PDF, DOC or DOCX resume");
        if (resume.Length > 5 * 1024 * 1024) throw new ApiException(400, "Resume must be smaller than 5 MB");

        string savedPath;
        if (sharePoint.Enabled)
        {
            await using var memory = new MemoryStream();
            await resume.CopyToAsync(memory, cancellationToken);
            var uploaded = await sharePoint.UploadCandidateResumeAsync(user, resume.FileName, memory.ToArray(),
                resume.ContentType ?? "application/octet-stream", cancellationToken);
            savedPath = uploaded.WebUrl ?? $"sharepoint-item:{uploaded.Id}";
        }
        else
        {
            savedPath = await storage.SaveResumeAsync(user.Id, resume, cancellationToken);
        }
        user.ResumeName = resume.FileName;
        user.ResumePath = savedPath;
        await database.SaveChangesAsync(cancellationToken);
        return new MessageResponse($"Resume uploaded to {(sharePoint.Enabled ? "SharePoint" : "document storage")} successfully");
    }
}
