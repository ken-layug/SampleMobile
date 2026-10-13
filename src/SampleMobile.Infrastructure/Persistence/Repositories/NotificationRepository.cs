using Microsoft.EntityFrameworkCore;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Persistence.Repositories;

internal sealed class NotificationRepository(AppDbContext db) : INotificationRepository {
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        db.Notifications
            .Include(notification => notification.Recipients)
            .FirstOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);

    public Task<Notification?> GetByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken = default) =>
        db.Notifications
            .Include(notification => notification.Recipients)
            .FirstOrDefaultAsync(notification => notification.DeduplicationKey == deduplicationKey, cancellationToken);

    public async Task<PagedResult<InboxItem>> GetInboxAsync(InboxQuery query, CancellationToken cancellationToken = default) {
        var inbox =
            from recipient in db.NotificationRecipients.AsNoTracking()
            where recipient.UserId == query.UserId && (!query.UnreadOnly || recipient.ReadAt == null)
            join notification in db.Notifications.AsNoTracking() on recipient.NotificationId equals notification.Id
            select new { Notification = notification, recipient.ReadAt };

        var totalCount = await inbox.CountAsync(cancellationToken);
        var rows = await inbox
            .OrderByDescending(row => row.Notification.CreatedAt)
            .ThenByDescending(row => row.Notification.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(row => new {
                row.Notification.Id,
                row.Notification.Type,
                row.Notification.Title,
                row.Notification.Body,
                row.Notification.ResourceId,
                row.Notification.Metadata,
                row.Notification.CreatedAt,
                row.ReadAt
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new InboxItem(row.Id, row.Type.Value, row.Title, row.Body, row.ResourceId, row.Metadata, row.CreatedAt, row.ReadAt))
            .ToList();
        return new PagedResult<InboxItem>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<InboxItem?> GetInboxItemAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default) {
        var row = await (
                from recipient in db.NotificationRecipients.AsNoTracking()
                where recipient.UserId == userId && recipient.NotificationId == notificationId
                join notification in db.Notifications.AsNoTracking() on recipient.NotificationId equals notification.Id
                select new {
                    notification.Id,
                    notification.Type,
                    notification.Title,
                    notification.Body,
                    notification.ResourceId,
                    notification.Metadata,
                    notification.CreatedAt,
                    recipient.ReadAt
                })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new InboxItem(row.Id, row.Type.Value, row.Title, row.Body, row.ResourceId, row.Metadata, row.CreatedAt, row.ReadAt);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.NotificationRecipients.CountAsync(recipient => recipient.UserId == userId && recipient.ReadAt == null, cancellationToken);
}
