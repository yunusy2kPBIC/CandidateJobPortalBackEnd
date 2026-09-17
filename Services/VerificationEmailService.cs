using System.Security.Cryptography;
using System.Text;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Services;

public sealed class VerificationEmailService(
    PortalDbContext database,
    PortalOptions options,
    IEmailSender emailSender)
{
    public int MaxAttempts => options.EmailVerificationMaxAttempts;

    public async Task SendAccountCreatedAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        var nextStep = user.Role == PortalRoles.Student
            ? "You can now complete your Cooperative Training application."
            : "You can now complete your profile, upload your CV, and apply for available jobs.";
        await emailSender.SendAsync(
            user.Email,
            "Your PBICareerPosting account is ready",
            $"Hello {user.FirstName}, your email has been verified and your {user.Role} account is active. {nextStep}",
            cancellationToken);
    }

    public async Task<RegistrationPendingResponse> IssueAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
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

        await emailSender.SendAsync(
            user.Email,
            "Your PBICareerPosting verification code",
            $"Your verification code is {code}. It expires at {verification.ExpiresAt:O} UTC.",
            cancellationToken);

        return new RegistrationPendingResponse(
            user.Email,
            verification.ExpiresAt,
            verification.ResendAvailableAt,
            IsDevelopmentDelivery && options.ExposeDevelopmentVerificationCode ? code : null);
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

    private bool IsDevelopmentDelivery =>
        string.Equals(options.EmailDeliveryMode, "development", StringComparison.OrdinalIgnoreCase);
}
