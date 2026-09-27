using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize(Roles = PortalRoles.Student), Route("api/student/cooperative-training")]
public sealed class StudentCooperativeTrainingController(
    PortalDbContext database,
    CooperativeTrainingSubmissionService submissions) : PortalControllerBase
{
    [HttpGet]
    public async Task<StudentCooperativeTrainingStatusResponse> Status(CancellationToken cancellationToken)
    {
        var user = await CurrentStudentAsync(cancellationToken);
        return new StudentCooperativeTrainingStatusResponse(
            await submissions.FindLatestByEmailAsync(user.Email, cancellationToken));
    }

    [HttpPost]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<ActionResult<CooperativeTrainingResponse>> Submit(
        [FromForm] string payload,
        [FromForm] IFormFile transcript,
        [FromForm(Name = "university_request")] IFormFile universityRequest,
        CancellationToken cancellationToken)
    {
        var user = await CurrentStudentAsync(cancellationToken);
        if (await submissions.FindLatestByEmailAsync(user.Email, cancellationToken) is not null)
            throw new ApiException(409, "You have already submitted a cooperative training request");

        var request = submissions.ParsePayload(payload);
        var created = await submissions.CreateAsync(
            request,
            transcript,
            universityRequest,
            CurrentUserId,
            user.Email,
            user.FirstName,
            user.LastName,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<CooperativeTrainingResponse> Update(
        [FromForm] string payload,
        [FromForm] IFormFile? transcript,
        [FromForm(Name = "university_request")] IFormFile? universityRequest,
        CancellationToken cancellationToken)
    {
        var user = await CurrentStudentAsync(cancellationToken);
        var existing = await submissions.FindLatestByEmailAsync(user.Email, cancellationToken)
            ?? throw new ApiException(404, "No cooperative training request was found to edit");
        var request = submissions.ParsePayload(payload);
        return await submissions.UpdateAsync(
            existing,
            request,
            transcript,
            universityRequest,
            CurrentUserId,
            user.Email,
            user.FirstName,
            user.LastName,
            cancellationToken);
    }

    private async Task<Models.User> CurrentStudentAsync(CancellationToken cancellationToken) =>
        await database.Users.AsNoTracking().SingleOrDefaultAsync(
            user => user.Id == CurrentUserId && user.Role == PortalRoles.Student,
            cancellationToken)
        ?? throw new ApiException(403, "Student access required");
}
