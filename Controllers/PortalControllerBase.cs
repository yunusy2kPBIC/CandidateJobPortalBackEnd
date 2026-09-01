using System.Security.Claims;
using CandidatePortal.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace CandidatePortal.Api.Controllers;

[ApiController]
public abstract class PortalControllerBase : ControllerBase
{
    protected int CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid or expired token");

    protected string CurrentSessionId => User.FindFirstValue("session_id")
        ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid or expired token");
}
