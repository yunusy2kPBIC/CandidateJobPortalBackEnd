using System.Data;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Services;

public sealed class SharePointOutboxWorker(
    IServiceScopeFactory scopeFactory,
    PortalOptions options,
    ILogger<SharePointOutboxWorker> logger) : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.SharePointSyncEnabled)
        {
            logger.LogInformation("SharePoint outbox worker is disabled");
            return;
        }

        logger.LogInformation("SharePoint outbox worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await ClaimBatchAsync(stoppingToken);
                if (claimed.Count == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                foreach (var itemId in claimed)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessItemAsync(itemId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SharePoint outbox worker loop failed");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }

    private async Task<IReadOnlyList<long>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = PortalClock.UtcNow();
        var token = Guid.NewGuid().ToString("N");
        var rows = await database.SharePointOutbox
            .Where(value => value.ProcessedAt == null && value.NextAttemptAt <= now &&
                (value.LockedUntil == null || value.LockedUntil < now))
            .OrderBy(value => value.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.LockToken = token;
            row.LockedUntil = now.Add(LeaseDuration);
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows.Select(value => value.Id).ToArray();
    }

    private async Task ProcessItemAsync(long itemId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var synchronization = scope.ServiceProvider.GetRequiredService<SharePointSyncService>();
        var item = await database.SharePointOutbox.SingleOrDefaultAsync(
            value => value.Id == itemId && value.ProcessedAt == null,
            cancellationToken);
        if (item is null) return;

        try
        {
            await SynchronizeAsync(database, synchronization, item, cancellationToken);
            item.ProcessedAt = PortalClock.UtcNow();
            item.Content = null;
            item.LastError = null;
            item.LockToken = null;
            item.LockedUntil = null;
            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Processed SharePoint outbox item {OutboxItemId} ({Operation})",
                item.Id, item.Operation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            item.LockToken = null;
            item.LockedUntil = null;
            await database.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            item.Attempts += 1;
            item.LastError = Truncate(exception.Message, 2000);
            item.NextAttemptAt = PortalClock.UtcNow().Add(RetryDelay(item.Attempts));
            item.LockToken = null;
            item.LockedUntil = null;
            await database.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(exception,
                "SharePoint outbox item {OutboxItemId} failed; retry {Attempt} at {NextAttemptAt}",
                item.Id, item.Attempts, item.NextAttemptAt);
        }
    }

    private static async Task SynchronizeAsync(
        PortalDbContext database,
        SharePointSyncService synchronization,
        SharePointOutboxItem item,
        CancellationToken cancellationToken)
    {
        switch (item.Operation)
        {
            case SharePointOutboxOperations.CandidateUpsert:
            {
                var candidate = await database.Users.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == item.EntityId, cancellationToken);
                if (candidate is not null)
                    await synchronization.SyncCandidateAsync(candidate, cancellationToken);
                break;
            }
            case SharePointOutboxOperations.JobUpsert:
            {
                var job = await database.Jobs.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == item.EntityId, cancellationToken);
                if (job is not null)
                    await synchronization.SyncJobAsync(job, cancellationToken);
                break;
            }
            case SharePointOutboxOperations.JobDelete:
                await synchronization.DeleteJobByPortalIdAsync(item.EntityId, cancellationToken);
                break;
            case SharePointOutboxOperations.ApplicationUpsert:
            {
                var application = await database.Applications.AsNoTracking()
                    .Include(value => value.User)
                    .Include(value => value.Job)
                    .SingleOrDefaultAsync(value => value.Id == item.EntityId, cancellationToken);
                if (application is not null)
                    await synchronization.SyncApplicationAsync(application, cancellationToken);
                break;
            }
            case SharePointOutboxOperations.ResumeUpload:
            {
                var candidate = await database.Users.SingleOrDefaultAsync(
                    value => value.Id == item.EntityId, cancellationToken);
                if (candidate is null) break;
                if (!string.Equals(candidate.ResumePath, PendingResumePath(item.Id),
                        StringComparison.OrdinalIgnoreCase))
                    break;
                if (item.Content is null || string.IsNullOrWhiteSpace(item.FileName))
                    throw new InvalidOperationException("Queued resume content is unavailable");
                var uploaded = await synchronization.UploadCandidateResumeAsync(
                    candidate,
                    item.FileName,
                    item.Content,
                    item.ContentType ?? "application/octet-stream",
                    item.Id.ToString(),
                    cancellationToken);
                candidate.ResumePath = uploaded.WebUrl ?? $"sharepoint-item:{uploaded.Id}";
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported SharePoint outbox operation: {item.Operation}");
        }
    }

    public static string PendingResumePath(long outboxItemId) =>
        $"sharepoint-pending:{outboxItemId}";

    private static TimeSpan RetryDelay(int attempts)
    {
        var seconds = Math.Min(3600, 5 * Math.Pow(2, Math.Min(attempts - 1, 10)));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
