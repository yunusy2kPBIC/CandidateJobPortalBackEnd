using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CandidatePortal.Api.Security;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TokenService tokenService,
    PortalDbContext database)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            Context.Items["authentication_detail"] = "Authentication required";
            return AuthenticateResult.NoResult();
        }

        var claims = tokenService.Decode(authorization["Bearer ".Length..].Trim());
        if (claims is null)
        {
            Context.Items["authentication_detail"] = "Invalid or expired token";
            return AuthenticateResult.Fail("Invalid or expired token");
        }

        var session = await database.AuthSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == claims.SessionId);
        if (session is null || session.RevokedAt != null || session.ExpiresAt <= PortalClock.UtcNow() ||
            session.UserId != claims.UserId)
        {
            Context.Items["authentication_detail"] = "Invalid or expired token";
            return AuthenticateResult.Fail("Invalid or expired token");
        }

        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == claims.UserId);
        if (user is null)
        {
            Context.Items["authentication_detail"] = "Invalid or expired token";
            return AuthenticateResult.Fail("Invalid or expired token");
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("session_id", session.Id),
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        var detail = Context.Items["authentication_detail"] as string ?? "Authentication required";
        await Response.WriteAsync(JsonSerializer.Serialize(new { detail }));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        await Response.WriteAsync(JsonSerializer.Serialize(new { detail = "Administrator access required" }));
    }
}
