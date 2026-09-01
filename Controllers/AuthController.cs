using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    PortalDbContext database,
    PasswordHasher passwordHasher,
    TokenService tokenService,
    SharePointSyncService sharePoint) : PortalControllerBase
{
    [AllowAnonymous, HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest payload, CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        if (email != payload.ConfirmEmail.Trim().ToLowerInvariant())
            throw new ApiException(400, "Email addresses do not match");
        if (payload.Password != payload.ConfirmPassword)
            throw new ApiException(400, "Passwords do not match");
        if (!payload.AcceptedTerms)
            throw new ApiException(400, "You must accept the terms of use");
        if (await database.Users.AnyAsync(user => user.Email == email, cancellationToken))
            throw new ApiException(409, "An account already exists for this email");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = new User
        {
            Email = email,
            PasswordHash = passwordHasher.Hash(payload.Password),
            FirstName = payload.FirstName.Trim(),
            LastName = payload.LastName.Trim(),
            CountryCode = payload.CountryCode,
            Phone = payload.Phone.Trim(),
            Country = payload.Country,
            City = "",
            Title = "Candidate",
            About = "",
            Role = "candidate",
            Preferences = new UserPreference(),
        };
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
        await sharePoint.SyncCandidateAsync(user, cancellationToken);
        var response = await IssueAuthResponse(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [AllowAnonymous, HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest payload, CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        var user = await database.Users.SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        if (user is null || !passwordHasher.Verify(payload.Password, user.PasswordHash))
            throw new ApiException(401, "Incorrect email or password");
        return await IssueAuthResponse(user, cancellationToken);
    }

    [Authorize, HttpPost("logout")]
    public async Task<ActionResult<MessageResponse>> Logout(CancellationToken cancellationToken)
    {
        var session = await database.AuthSessions.SingleOrDefaultAsync(value => value.Id == CurrentSessionId, cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        session.RevokedAt = PortalClock.UtcNow();
        await database.SaveChangesAsync(cancellationToken);
        return new MessageResponse("Signed out successfully");
    }

    [Authorize, HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        return user.ToResponse();
    }

    [Authorize, HttpPut("password")]
    public async Task<ActionResult<MessageResponse>> UpdatePassword(PasswordUpdateRequest payload, CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([CurrentUserId], cancellationToken)
            ?? throw new ApiException(401, "Invalid or expired token");
        if (!passwordHasher.Verify(payload.CurrentPassword, user.PasswordHash))
            throw new ApiException(400, "Current password is incorrect");
        if (payload.NewPassword != payload.ConfirmPassword)
            throw new ApiException(400, "New passwords do not match");
        user.PasswordHash = passwordHasher.Hash(payload.NewPassword);
        await database.SaveChangesAsync(cancellationToken);
        return new MessageResponse("Password updated successfully");
    }

    private async Task<AuthResponse> IssueAuthResponse(User user, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var expiresAt = tokenService.AccessTokenExpiry();
        database.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = user.Id, ExpiresAt = expiresAt });
        await database.SaveChangesAsync(cancellationToken);
        return new AuthResponse(tokenService.Create(user.Id, user.Role, sessionId, expiresAt), "bearer", user.ToResponse());
    }
}
