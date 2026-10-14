using SampleMobile.Mobile.Core.Api.Contracts;

namespace SampleMobile.Mobile.Core.Notifications;

/// <summary>
/// A notification as the app shows it. Always built from the API response, never from push payload text,
/// and identified by the backend id.
/// </summary>
public sealed record InboxNotification(
    Guid Id,
    string Type,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? ResourceId,
    IReadOnlyDictionary<string, string> Metadata) {
    private static readonly IReadOnlyDictionary<string, string> NoMetadata = new Dictionary<string, string>();

    public bool IsRead => ReadAt is not null;

    public static InboxNotification From(NotificationResponse response) => new(
        response.Id,
        response.Type,
        response.Title,
        response.Body,
        response.CreatedAt,
        response.ReadAt,
        response.ResourceId,
        response.Metadata ?? NoMetadata);
}
