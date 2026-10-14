namespace SampleMobile.Domain.Entities;

/// <summary>
/// One user's copy of a notification. Read state lives here, so each recipient reads
/// independently and nothing about it depends on whether any push was ever delivered.
/// </summary>
public sealed class NotificationRecipient {
    private NotificationRecipient() {
    }

    internal NotificationRecipient(Guid notificationId, Guid userId) {
        Id = Guid.CreateVersion7();
        NotificationId = notificationId;
        UserId = userId;
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    /// <summary>Marks the notification read. Idempotent: the first read time is kept.</summary>
    internal void MarkRead(DateTimeOffset now) {
        ReadAt ??= now;
    }
}
