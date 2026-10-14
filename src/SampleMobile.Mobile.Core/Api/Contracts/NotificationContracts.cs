namespace SampleMobile.Mobile.Core.Api.Contracts;

/// <summary>One notification addressed to the signed-in user. <see cref="ReadAt"/> is this user's read time.</summary>
public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? ResourceId,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>A page of the inbox. <see cref="UnreadCount"/> is the user's overall unread count.</summary>
public sealed record NotificationListResponse(
    IReadOnlyList<NotificationResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount);
