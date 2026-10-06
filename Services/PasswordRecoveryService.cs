using System.Security.Cryptography;
using System.Text;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Services;

public sealed class PasswordRecoveryService(
    PortalDbContext database,
    PortalOptions options,
    OtpAuditService otpAudit,
    IEmailSender emailSender)
{
    private const string GenericMessage =
        "If an active account exists for this email, a password reset verification code has been sent.";

    public int MaxAttempts => options.PasswordResetMaxAttempts;

    public PasswordRecoveryPendingResponse GenericResponse(string email)
    {
        var now = PortalClock.UtcNow();
        return new PasswordRecoveryPendingResponse(
            GenericMessage,
            email,
            now.AddMinutes(options.PasswordResetMinutes),
            now.AddSeconds(options.PasswordResetResendSeconds),
            null);
    }

    public async Task<PasswordRecoveryPendingResponse> IssueAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        var now = PortalClock.UtcNow();
        var reset = await database.PasswordResets
            .SingleOrDefaultAsync(value => value.UserId == user.Id, cancellationToken);
        if (reset is { ConsumedAt: null } && reset.ResendAvailableAt > now)
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((reset.ResendAvailableAt - now).TotalSeconds));
            throw new ApiException(429, $"Please wait {remainingSeconds} seconds before requesting another reset code");
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        if (reset is null)
        {
            reset = new PasswordReset { UserId = user.Id };
            database.PasswordResets.Add(reset);
        }
        var codeHash = Hash(user.Id, code);
        reset.CodeHash = codeHash;
        reset.AttemptCount = 0;
        reset.CreatedAt = now;
        reset.ExpiresAt = now.AddMinutes(options.PasswordResetMinutes);
        reset.ResendAvailableAt = now.AddSeconds(options.PasswordResetResendSeconds);
        reset.ConsumedAt = null;
        otpAudit.RecordIssued(
            user.Id,
            OtpPurposes.PasswordReset,
            codeHash,
            now,
            reset.ExpiresAt);
        await database.SaveChangesAsync(cancellationToken);

        var message = $"""
            Hello {user.FirstName},

            We received a request to reset your PBICareerPosting password. Use the verification code below to continue.

            Username: {user.Email}
            Verification code: {code}
            Code expires: {reset.ExpiresAt:dd MMM yyyy 'at' HH:mm} UTC

            Do not share this code with anyone. If you did not request a password reset, you can ignore this email and keep your existing password.

            Regards,
            PBICareerPosting Team
            """;
        await emailSender.SendAsync(
            user.Email,
            "PBICareerPosting password reset verification code",
            message,
            EmailPurposes.PasswordReset,
            cancellationToken);

        return new PasswordRecoveryPendingResponse(
            GenericMessage,
            user.Email,
            reset.ExpiresAt,
            reset.ResendAvailableAt,
            ShouldExposeCode ? code : null);
    }

    public bool Matches(int userId, string code, string expectedHash)
    {
        var expected = Convert.FromHexString(expectedHash);
        var actual = Convert.FromHexString(Hash(userId, code));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public Task SendPasswordChangedAsync(User user, CancellationToken cancellationToken = default)
    {
        var message = $"""
            Hello {user.FirstName},

            Your PBICareerPosting password was updated successfully.

            Username: {user.Email}

            All existing sessions have been signed out. You can now sign in using your new password. If you did not make this change, contact candidate support immediately.

            Regards,
            PBICareerPosting Team
            """;
        return emailSender.SendAsync(
            user.Email,
            "Your PBICareerPosting password was changed",
            message,
            EmailPurposes.PasswordChanged,
            cancellationToken);
    }

    private string Hash(int userId, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"password-reset:{userId}:{code}")));
    }

    private bool ShouldExposeCode => !options.EmailEnabled;
}
