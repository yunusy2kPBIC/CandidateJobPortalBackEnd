using System.Security.Cryptography;
using System.Text;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Services;

public sealed class VerificationEmailService(
    PortalDbContext database,
    PortalOptions options,
    ILogger<VerificationEmailService> logger)
{
    public int MaxAttempts => options.EmailVerificationMaxAttempts;

    public async Task<RegistrationPendingResponse> IssueAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(options.EmailDeliveryMode, "development", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(503, "Email delivery is not configured");

        var now = PortalClock.UtcNow();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var verification = await database.EmailVerifications
            .SingleOrDefaultAsync(value => value.UserId == user.Id, cancellationToken);
        if (verification is null)
        {
            verification = new EmailVerification { UserId = user.Id };
            database.EmailVerifications.Add(verification);
        }
        verification.CodeHash = Hash(user.Id, code);
        verification.AttemptCount = 0;
        verification.CreatedAt = now;
        verification.ExpiresAt = now.AddMinutes(options.EmailVerificationMinutes);
        verification.ResendAvailableAt = now.AddSeconds(options.EmailVerificationResendSeconds);
        verification.ConsumedAt = null;
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "DEV EMAIL from {Sender} to {Recipient}: PBICareerPosting verification code {VerificationCode}. It expires at {ExpiresAt} UTC.",
            options.EmailSenderAddress,
            user.Email,
            code,
            verification.ExpiresAt);

        return new RegistrationPendingResponse(
            user.Email,
            verification.ExpiresAt,
            verification.ResendAvailableAt,
            options.ExposeDevelopmentVerificationCode ? code : null);
    }

    public bool Matches(int userId, string code, string expectedHash)
    {
        var expected = Convert.FromHexString(expectedHash);
        var actual = Convert.FromHexString(Hash(userId, code));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private string Hash(int userId, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{userId}:{code}")));
    }
}
