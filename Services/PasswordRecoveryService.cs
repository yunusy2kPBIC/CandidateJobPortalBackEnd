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
    ILogger<PasswordRecoveryService> logger)
{
    private const string GenericMessage =
        "If an active account exists for this email, password reset instructions have been generated.";

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
        EnsureDevelopmentDelivery();
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
        reset.CodeHash = Hash(user.Id, code);
        reset.AttemptCount = 0;
        reset.CreatedAt = now;
        reset.ExpiresAt = now.AddMinutes(options.PasswordResetMinutes);
        reset.ResendAvailableAt = now.AddSeconds(options.PasswordResetResendSeconds);
        reset.ConsumedAt = null;
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "DEV EMAIL from {Sender} to {Recipient}. Subject: Reset your PBICareerPosting password. " +
            "Your password reset code is {ResetCode}. It expires at {ExpiresAt} UTC.",
            options.EmailSenderAddress,
            user.Email,
            code,
            reset.ExpiresAt);

        return new PasswordRecoveryPendingResponse(
            GenericMessage,
            user.Email,
            reset.ExpiresAt,
            reset.ResendAvailableAt,
            options.ExposeDevelopmentVerificationCode ? code : null);
    }

    public bool Matches(int userId, string code, string expectedHash)
    {
        var expected = Convert.FromHexString(expectedHash);
        var actual = Convert.FromHexString(Hash(userId, code));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public Task SendPasswordChangedAsync(User user, CancellationToken cancellationToken = default)
    {
        EnsureDevelopmentDelivery();
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "DEV EMAIL from {Sender} to {Recipient}. Subject: Your PBICareerPosting password was changed. " +
            "Hello {FirstName}, your password has been reset successfully. If you did not make this change, contact candidate support.",
            options.EmailSenderAddress,
            user.Email,
            user.FirstName);
        return Task.CompletedTask;
    }

    private string Hash(int userId, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"password-reset:{userId}:{code}")));
    }

    private void EnsureDevelopmentDelivery()
    {
        if (!string.Equals(options.EmailDeliveryMode, "development", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(503, "Email delivery is not configured");
    }
}
