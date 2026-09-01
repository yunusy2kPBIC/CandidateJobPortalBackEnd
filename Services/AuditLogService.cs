using CandidatePortal.Api.Data;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Services;

public sealed class AuditLogService(PortalDbContext database)
{
    public void Add(int adminUserId, string action, string entityType, string? entityId, string details)
    {
        database.AuditLogs.Add(new AuditLog
        {
            AdminUserId = adminUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
        });
    }

    public async Task RecordAsync(
        int adminUserId,
        string action,
        string entityType,
        string? entityId,
        string details,
        CancellationToken cancellationToken = default)
    {
        Add(adminUserId, action, entityType, entityId, details);
        await database.SaveChangesAsync(cancellationToken);
    }
}
