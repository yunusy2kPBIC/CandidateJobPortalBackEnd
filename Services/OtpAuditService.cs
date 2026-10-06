using CandidatePortal.Api.Data;
using CandidatePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Services;

public sealed class OtpAuditService(PortalDbContext database)
{
    public void RecordIssued(
        int userId,
        string purpose,
        string codeHash,
        DateTime createdAt,
        DateTime expiresAt)
    {
        database.OtpLogs.Add(new OtpLog
        {
            UserId = userId,
            Purpose = purpose,
            CodeHash = codeHash,
            Status = OtpStatuses.Issued,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        });
    }

    public async Task RecordAttemptAsync(
        int userId,
        string purpose,
        string codeHash,
        bool consumed,
        CancellationToken cancellationToken = default)
    {
        var entry = await FindLatestAsync(userId, purpose, codeHash, cancellationToken);
        if (entry is null) return;
        entry.AttemptCount += 1;
        if (consumed)
        {
            entry.Status = OtpStatuses.Consumed;
            entry.ConsumedAt = PortalClock.UtcNow();
        }
    }

    public async Task MarkStatusAsync(
        int userId,
        string purpose,
        string codeHash,
        string status,
        CancellationToken cancellationToken = default)
    {
        var entry = await FindLatestAsync(userId, purpose, codeHash, cancellationToken);
        if (entry is not null) entry.Status = status;
    }

    private Task<OtpLog?> FindLatestAsync(
        int userId,
        string purpose,
        string codeHash,
        CancellationToken cancellationToken) =>
        database.OtpLogs
            .Where(entry => entry.UserId == userId && entry.Purpose == purpose && entry.CodeHash == codeHash)
            .OrderByDescending(entry => entry.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
