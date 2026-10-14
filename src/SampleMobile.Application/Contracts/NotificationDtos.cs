using SampleMobile.Application.Abstractions.Repositories;

namespace SampleMobile.Application.Contracts;

/// <summary>A notification in the signed-in user's inbox. This record, not the push payload, is authoritative.</summary>
/// <param name="Id">Notification id; the same value arrives as <c>notificationId</c> in push data.</param>
/// <param name="Type">Category such as <c>demo.alert</c>. Ignore types you don't recognize.</param>
/// <param name="Title">Display title.</param>
/// <param name="Body">Display body.</param>
/// <param name="CreatedAt">When the notification was created (UTC).</param>
/// <param name="ReadAt">When this user marked it read; <c>null</c> while unread.</param>
/// <param name="ResourceId">Optional id of the business object the notification is about.</param>
/// <param name="Metadata">Optional string key/value pairs.</param>
public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? ResourceId,
    IReadOnlyDictionary<string, string> Metadata) {
    public static NotificationResponse FromInboxItem(InboxItem item) =>
        new(item.NotificationId, item.Type, item.Title, item.Body, item.CreatedAt, item.ReadAt, item.ResourceId, item.Metadata);
}

/// <summary>One page of the signed-in user's inbox, newest first.</summary>
/// <param name="Items">The notifications on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size.</param>
/// <param name="TotalCount">Notifications matching the filter across all pages.</param>
/// <param name="UnreadCount">Unread notifications for the user overall (independent of the filter), for badges.</param>
public sealed record NotificationListResponse(
    IReadOnlyList<NotificationResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount);
