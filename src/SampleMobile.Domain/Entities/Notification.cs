using SampleMobile.Domain.Primitives;

namespace SampleMobile.Domain.Entities;

/// <summary>
/// The canonical, application-owned notification. It exists independently of any push
/// provider: delivery jobs reference it, but its content and read state never depend on them.
/// </summary>
public sealed class Notification : AggregateRoot {
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 2000;
    public const int MaxResourceIdLength = 200;
    public const int MaxDeduplicationKeyLength = 200;
    public const int MaxMetadataEntries = 20;
    public const int MaxMetadataKeyLength = 64;
    public const int MaxMetadataValueLength = 500;

    private readonly List<NotificationRecipient> _recipients = [];

    private Notification() {
    }

    public Guid Id { get; private set; }
    public NotificationType Type { get; private set; } = null!;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? ResourceId { get; private set; }
    public IReadOnlyDictionary<string, string> Metadata { get; private set; } = new Dictionary<string, string>();

    /// <summary>
    /// Identifies the business fact that produced this notification (e.g. an outbox event id).
    /// Unique, so re-processing the same fact can never create a second notification.
    /// </summary>
    public string? DeduplicationKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<NotificationRecipient> Recipients => _recipients;

    /// <summary>
    /// Creates a notification for one or more recipients. Callers validate user input first;
    /// the guards here only catch programming errors.
    /// </summary>
    public static Notification Create(
        NotificationType type,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? metadata,
        string? resourceId,
        IReadOnlyCollection<Guid> recipientUserIds,
        string? deduplicationKey,
        DateTimeOffset now) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(title.Trim().Length, MaxTitleLength, nameof(title));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(body.Trim().Length, MaxBodyLength, nameof(body));
        if (resourceId is not null) {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(resourceId.Length, MaxResourceIdLength, nameof(resourceId));
        }

        if (deduplicationKey is not null) {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                deduplicationKey.Length, MaxDeduplicationKeyLength, nameof(deduplicationKey));
        }

        var distinctRecipients = recipientUserIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinctRecipients.Count == 0) {
            throw new ArgumentException("A notification needs at least one recipient.", nameof(recipientUserIds));
        }

        var notification = new Notification {
            Id = Guid.CreateVersion7(),
            Type = type,
            Title = title.Trim(),
            Body = body.Trim(),
            ResourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim(),
            Metadata = new Dictionary<string, string>(metadata ?? new Dictionary<string, string>()),
            DeduplicationKey = deduplicationKey,
            CreatedAt = now
        };

        foreach (var userId in distinctRecipients) {
            notification._recipients.Add(new NotificationRecipient(notification.Id, userId));
        }

        return notification;
    }

    public NotificationRecipient? FindRecipient(Guid userId) =>
        _recipients.FirstOrDefault(recipient => recipient.UserId == userId);

    /// <summary>Marks the notification read for one recipient. Idempotent.</summary>
    public NotificationRecipient MarkReadBy(Guid userId, DateTimeOffset now) {
        var recipient = FindRecipient(userId)
            ?? throw new InvalidOperationException($"User {userId} is not a recipient of notification {Id}.");
        recipient.MarkRead(now);
        return recipient;
    }
}
