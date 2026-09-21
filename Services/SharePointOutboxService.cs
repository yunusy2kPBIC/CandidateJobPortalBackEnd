using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Services;

public sealed class SharePointOutboxService(PortalDbContext database, PortalOptions options)
{
    public bool Enabled => options.SharePointSyncEnabled;

    public void EnqueueCandidate(int candidateId) =>
        Enqueue(SharePointOutboxOperations.CandidateUpsert, candidateId);

    public void EnqueueJob(int jobId) =>
        Enqueue(SharePointOutboxOperations.JobUpsert, jobId);

    public void EnqueueJobDelete(int jobId) =>
        Enqueue(SharePointOutboxOperations.JobDelete, jobId);

    public void EnqueueApplication(int applicationId) =>
        Enqueue(SharePointOutboxOperations.ApplicationUpsert, applicationId);

    public SharePointOutboxItem? EnqueueResume(
        int candidateId, string fileName, string contentType, byte[] content)
    {
        if (!Enabled) return null;
        var item = NewItem(SharePointOutboxOperations.ResumeUpload, candidateId);
        item.FileName = Path.GetFileName(fileName);
        item.ContentType = contentType;
        item.Content = content;
        database.SharePointOutbox.Add(item);
        return item;
    }

    private void Enqueue(string operation, int entityId)
    {
        if (!Enabled) return;
        database.SharePointOutbox.Add(NewItem(operation, entityId));
    }

    private static SharePointOutboxItem NewItem(string operation, int entityId)
    {
        var now = PortalClock.UtcNow();
        return new SharePointOutboxItem
        {
            Operation = operation,
            EntityId = entityId,
            CreatedAt = now,
            NextAttemptAt = now,
        };
    }
}
