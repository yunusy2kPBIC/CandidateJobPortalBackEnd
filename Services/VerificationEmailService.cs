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
    OtpAuditService otpAudit,
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
        var message = $"""
            Hello {user.FirstName},

            Your PBICareerPosting registration is complete and your account is now active.

            Username: {user.Email}
            Account type: {user.Role}

            {nextStep}

            If you did not create this account, please contact candidate support.

            Regards,
            PBICareerPosting Team
            """;
        await emailSender.SendAsync(
            user.Email,
            "Your PBICareerPosting account is ready",
            message,
            EmailPurposes.RegistrationConfirmation,
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
        var codeHash = Hash(user.Id, code);
        verification.CodeHash = codeHash;
        verification.AttemptCount = 0;
        verification.CreatedAt = now;
        verification.ExpiresAt = now.AddMinutes(options.EmailVerificationMinutes);
        verification.ResendAvailableAt = now.AddSeconds(options.EmailVerificationResendSeconds);
        verification.ConsumedAt = null;
        otpAudit.RecordIssued(
            user.Id,
            OtpPurposes.EmailVerification,
            codeHash,
            now,
            verification.ExpiresAt);
        await database.SaveChangesAsync(cancellationToken);

        var message = $"""
            Hello {user.FirstName},

            Thank you for registering with PBICareerPosting. Use the verification code below to activate your account.

            Username: {user.Email}
            Verification code: {code}
            Code expires: {verification.ExpiresAt:dd MMM yyyy 'at' HH:mm} UTC

            Do not share this code with anyone. If you did not create this account, you can ignore this email.

            Regards,
            PBICareerPosting Team
            """;
        await emailSender.SendAsync(
            user.Email,
            "Verify your PBICareerPosting registration",
            message,
            EmailPurposes.RegistrationVerification,
            cancellationToken);

        return new RegistrationPendingResponse(
            user.Email,
            verification.ExpiresAt,
            verification.ResendAvailableAt,
            ShouldExposeCode ? code : null);
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

    private bool ShouldExposeCode => !options.EmailEnabled;
}
