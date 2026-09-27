using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    PortalDbContext database,
    PasswordHasher passwordHasher,
    PortalSignInService signInService,
    SharePointOutboxService sharePointOutbox,
    MasterDataService masterData,
    VerificationEmailService verificationEmails,
    PasswordRecoveryService passwordRecovery,
    PrivacyNoticeService privacyNotice) : PortalControllerBase
{
    [AllowAnonymous, HttpPost("email-availability"), EnableRateLimiting("email-availability")]
    public async Task<ActionResult<EmailAvailabilityResponse>> EmailAvailability(
        EmailAvailabilityRequest payload,
        CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        var existing = await database.Users.AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => new { user.IsEmailVerified })
            .SingleOrDefaultAsync(cancellationToken);
        return new EmailAvailabilityResponse(existing is null, existing is { IsEmailVerified: false });
    }

    [AllowAnonymous, HttpPost("register")]
    public async Task<ActionResult<RegistrationPendingResponse>> Register(
        RegisterRequest payload,
        CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        if (email != payload.ConfirmEmail.Trim().ToLowerInvariant())
            throw new ApiException(400, "Email addresses do not match");
        if (payload.Password != payload.ConfirmPassword)
            throw new ApiException(400, "Passwords do not match");
        if (!payload.AcceptedTerms)
            throw new ApiException(400, "You must accept the privacy notice and terms of use");
        if (!string.Equals(payload.PrivacyVersion.Trim(), privacyNotice.CurrentVersion, StringComparison.Ordinal))
            throw new ApiException(409, "The privacy notice has changed. Review and accept the current version before registering.");
        var nationality = payload.Nationality.Trim();
        if (!payload.IsStudent)
            await masterData.ValidateNationalityAsync(nationality, cancellationToken);
        var gender = payload.Gender.Trim();
        if (!PortalValues.Genders.Contains(gender))
            throw new ApiException(400, "Select a valid gender");
        await masterData.ValidateResidenceCountryAsync(payload.Country, cancellationToken);
        var existing = await database.Users.AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => new { user.IsEmailVerified })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsEmailVerified)
                throw new ApiException(409, "Email verification is already pending. Continue to the verification screen.");
            throw new ApiException(409, "An account already exists for this email");
        }

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
            Nationality = payload.IsStudent ? "" : nationality,
            Gender = gender,
            City = "",
            Title = payload.IsStudent ? "Student" : "Candidate",
            About = "",
            Role = payload.IsStudent ? PortalRoles.Student : PortalRoles.Candidate,
            IsEmailVerified = false,
            Preferences = new UserPreference(),
        };
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
        var userAgent = Request.Headers.UserAgent.ToString();
        database.UserConsents.Add(new UserConsent
        {
            UserId = user.Id,
            DocumentType = "privacy_notice",
            DocumentVersion = privacyNotice.CurrentVersion,
            AcceptedAt = PortalClock.UtcNow(),
            IpAddress = Truncate(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", 64),
            UserAgent = Truncate(userAgent, 500),
        });
        await database.SaveChangesAsync(cancellationToken);
        var response = await verificationEmails.IssueAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [AllowAnonymous, HttpPost("verify-email"), EnableRateLimiting("email-verification")]
    public async Task<ActionResult<AuthResponse>> VerifyEmail(
        VerifyEmailRequest payload,
        CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        var user = await database.Users.Include(value => value.EmailVerification)
            .SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        var verification = user?.EmailVerification;
        if (user is null || user.IsEmailVerified || verification is null || verification.ConsumedAt is not null)
            throw new ApiException(400, "Verification code is invalid or expired");

        var now = PortalClock.UtcNow();
        if (verification.ExpiresAt <= now)
            throw new ApiException(400, "Verification code has expired. Request a new code.");
        if (verification.AttemptCount >= verificationEmails.MaxAttempts)
            throw new ApiException(429, "Too many verification attempts. Request a new code.");

        verification.AttemptCount += 1;
        if (!verificationEmails.Matches(user.Id, payload.Code, verification.CodeHash))
        {
            await database.SaveChangesAsync(cancellationToken);
            throw new ApiException(400, "Verification code is incorrect");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        user.IsEmailVerified = true;
        verification.ConsumedAt = now;
        sharePointOutbox.EnqueueCandidate(user.Id);
        await database.SaveChangesAsync(cancellationToken);
        var response = await signInService.IssueAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await verificationEmails.SendAccountCreatedAsync(user, cancellationToken);
        return response;
    }

    [AllowAnonymous, HttpPost("resend-verification"), EnableRateLimiting("email-verification")]
    public async Task<ActionResult<RegistrationPendingResponse>> ResendVerification(
        ResendVerificationRequest payload,
        CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        var user = await database.Users.Include(value => value.EmailVerification)
            .SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        if (user is null || user.IsEmailVerified)
            throw new ApiException(400, "No pending verification was found for this email");

        var now = PortalClock.UtcNow();
        if (user.EmailVerification is { ResendAvailableAt: var resendAt } && resendAt > now)
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((resendAt - now).TotalSeconds));
            throw new ApiException(429, $"Please wait {remainingSeconds} seconds before requesting another code");
        }

        return await verificationEmails.IssueAsync(user, cancellationToken);
    }

    [AllowAnonymous, HttpPost("password-recovery/request"), EnableRateLimiting("password-recovery")]
    public async Task<ActionResult<PasswordRecoveryPendingResponse>> RequestPasswordRecovery(
        PasswordRecoveryRequest payload,
        CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        var user = await database.Users.SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return passwordRecovery.GenericResponse(email);
        return await passwordRecovery.IssueAsync(user, cancellationToken);
    }

    [AllowAnonymous, HttpPost("password-recovery/reset"), EnableRateLimiting("password-recovery")]
    public async Task<ActionResult<MessageResponse>> ResetPassword(
        PasswordResetRequest payload,
        CancellationToken cancellationToken)
    {
        if (payload.NewPassword != payload.ConfirmPassword)
            throw new ApiException(400, "Passwords do not match");
        ValidatePasswordStrength(payload.NewPassword);

        var email = payload.Email.Trim().ToLowerInvariant();
        var user = await database.Users.Include(value => value.PasswordReset)
            .SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        var reset = user?.PasswordReset;
        if (user is null || !user.IsEmailVerified || reset is null || reset.ConsumedAt is not null)
            throw new ApiException(400, "Password reset code is invalid or expired");

        var now = PortalClock.UtcNow();
        if (reset.ExpiresAt <= now)
            throw new ApiException(400, "Password reset code has expired. Request a new code.");
        if (reset.AttemptCount >= passwordRecovery.MaxAttempts)
            throw new ApiException(429, "Too many reset attempts. Request a new code.");

        reset.AttemptCount += 1;
        if (!passwordRecovery.Matches(user.Id, payload.Code, reset.CodeHash))
        {
            await database.SaveChangesAsync(cancellationToken);
            throw new ApiException(400, "Password reset code is incorrect");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        user.PasswordHash = passwordHasher.Hash(payload.NewPassword);
        reset.ConsumedAt = now;
        await database.SaveChangesAsync(cancellationToken);
        await database.AuthSessions
            .Where(value => value.UserId == user.Id && value.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.RevokedAt, now),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await passwordRecovery.SendPasswordChangedAsync(user, cancellationToken);
        return new MessageResponse("Password reset successfully. You can now sign in.");
    }

    [AllowAnonymous, HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest payload, CancellationToken cancellationToken)
    {
        var email = payload.Email.Trim().ToLowerInvariant();
        var user = await database.Users.SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        if (user is null || !passwordHasher.Verify(payload.Password, user.PasswordHash))
            throw new ApiException(401, "Incorrect email or password");
        if (!user.IsEmailVerified)
            throw new ApiException(403, "Verify your email before signing in");
        return await signInService.IssueAsync(user, cancellationToken);
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

    private static void ValidatePasswordStrength(string password)
    {
        if (password.Length < 8 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(value => !char.IsLetterOrDigit(value)))
        {
            throw new ApiException(
                400,
                "Password must contain at least eight characters, including uppercase, lowercase, number, and special character");
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

}
