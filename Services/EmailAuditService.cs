using CandidatePortal.Api.Data;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Services;

public sealed class EmailAuditService(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailAuditService> logger)
{
    public async Task<long?> StartAsync(
        string recipientAddress,
        string purpose,
        string subject,
        string status = DeliveryStatuses.Pending,
        string? reason = null)
    {
        try
        {
            var normalizedRecipient = recipientAddress.Trim().ToLowerInvariant();
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var now = PortalClock.UtcNow();
            var entry = new EmailLog
            {
                RecipientAddress = normalizedRecipient,
                Purpose = purpose,
                Subject = subject,
                Status = status,
                FailureReason = Truncate(reason),
                CreatedAt = now,
                CompletedAt = status == DeliveryStatuses.Pending ? null : now,
            };
            database.EmailLogs.Add(entry);
            await database.SaveChangesAsync();
            return entry.Id;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to create an email delivery log");
            return null;
        }
    }

    public async Task CompleteAsync(long? id, string status, string? reason = null)
    {
        if (id is null) return;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var entry = await database.EmailLogs.FindAsync(id.Value);
            if (entry is null) return;
            entry.Status = status;
            entry.FailureReason = Truncate(reason);
            entry.CompletedAt = PortalClock.UtcNow();
            await database.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to complete email delivery log {EmailLogId}", id);
        }
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= 2000 ? value : value[..2000];
}
