using Microsoft.Extensions.Logging;
using NhatVuong.Application.Abstractions;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Notifications;

/// <summary>
/// Stores in-app notifications and hands them to the external delivery port (FR-E2, FR-A5).
/// Rows are added to the caller's unit of work; the caller saves.
/// </summary>
public sealed class NotificationService(
    IDataStore store,
    INotificationPort port,
    TimeProvider time,
    ILogger<NotificationService> logger)
{
    public async Task NotifyRolesAsync(
        IReadOnlyCollection<UserRole> roles, string category, string title, string body, Guid? incidentId, CancellationToken ct = default)
    {
        var recipients = await store.Query<User>().Where(u => u.IsActive && roles.Contains(u.Role)).ToListAsync(ct);
        await NotifyAsync(recipients, category, title, body, incidentId, ct);
    }

    public async Task NotifyUsersAsync(
        IReadOnlyCollection<Guid> userIds, string category, string title, string body, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        var recipients = await store.Query<User>().Where(u => u.IsActive && userIds.Contains(u.Id)).ToListAsync(ct);
        await NotifyAsync(recipients, category, title, body, null, ct);
    }

    public Task<List<Notification>> ListAsync(Guid userId, bool unreadOnly, CancellationToken ct = default)
    {
        var query = store.Query<Notification>().Where(n => n.RecipientUserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        return query.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(ct);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) =>
        store.CountAsync(store.Query<Notification>().Where(n => n.RecipientUserId == userId && n.ReadAt == null), ct);

    public async Task MarkReadAsync(Guid userId, Guid? notificationId, CancellationToken ct = default)
    {
        var query = store.Query<Notification>().Where(n => n.RecipientUserId == userId && n.ReadAt == null);
        if (notificationId is { } id)
        {
            query = query.Where(n => n.Id == id);
        }

        var now = time.GetUtcNow();
        foreach (var notification in await query.ToListAsync(ct))
        {
            notification.ReadAt = now;
        }

        await store.SaveChangesAsync(ct);
    }

    private async Task NotifyAsync(
        IReadOnlyList<User> recipients, string category, string title, string body, Guid? incidentId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        foreach (var user in recipients)
        {
            store.Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                RecipientUserId = user.Id,
                Category = category,
                Title = title,
                Body = body,
                IncidentId = incidentId,
                CreatedAt = now,
            });
        }

        try
        {
            await port.DeliverAsync(new OutboundNotification(category, title, body, incidentId), recipients, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // External delivery is best effort; the in-app notification is the record.
            logger.LogWarning(ex, "External notification delivery failed for {Title}", title);
        }
    }
}
