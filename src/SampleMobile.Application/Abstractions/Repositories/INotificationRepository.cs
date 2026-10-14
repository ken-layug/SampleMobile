using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Abstractions.Repositories;

public interface INotificationRepository {
    void Add(Notification notification);

    /// <summary>Loads the aggregate (with recipients) for modification.</summary>
    Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task<Notification?> GetByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken = default);

    /// <summary>One user's inbox, newest first.</summary>
    Task<PagedResult<InboxItem>> GetInboxAsync(InboxQuery query, CancellationToken cancellationToken = default);

    /// <summary>One notification as seen by one recipient; <c>null</c> when the user is not a recipient.</summary>
    Task<InboxItem?> GetInboxItemAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record InboxQuery(Guid UserId, int Page, int PageSize, bool UnreadOnly);

/// <summary>A notification projected for one recipient, including that recipient's read state.</summary>
public sealed record InboxItem(
    Guid NotificationId,
    string Type,
    string Title,
    string Body,
    string? ResourceId,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);
