using System.ComponentModel.DataAnnotations;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[Authorize, Route("api/notifications")]
public sealed class NotificationsController(PortalDbContext database) : PortalControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationResponse>>> List(
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var notifications = await database.Notifications.AsNoTracking()
            .Where(value => value.UserId == CurrentUserId)
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return notifications.Select(value => value.ToResponse()).ToArray();
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<NotificationCountResponse>> UnreadCount(CancellationToken cancellationToken)
    {
        var count = await database.Notifications.CountAsync(
            value => value.UserId == CurrentUserId && !value.IsRead, cancellationToken);
        return new NotificationCountResponse(count);
    }

    [HttpPatch("{notificationId:int}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(
        int notificationId, CancellationToken cancellationToken)
    {
        var notification = await database.Notifications.SingleOrDefaultAsync(
            value => value.Id == notificationId && value.UserId == CurrentUserId, cancellationToken)
            ?? throw new ApiException(404, "Notification not found");
        notification.IsRead = true;
        await database.SaveChangesAsync(cancellationToken);
        return notification.ToResponse();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<MessageResponse>> MarkAllRead(CancellationToken cancellationToken)
    {
        await database.Notifications
            .Where(value => value.UserId == CurrentUserId && !value.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsRead, true), cancellationToken);
        return new MessageResponse("All notifications marked as read");
    }
}
