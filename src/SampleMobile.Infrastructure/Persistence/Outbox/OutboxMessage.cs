namespace SampleMobile.Infrastructure.Persistence.Outbox;

public enum OutboxMessageStatus {
    Pending,
    Processed,

    /// <summary>Gave up after the maximum number of attempts; needs a human to look at <see cref="OutboxMessage.LastError"/>.</summary>
    Failed
}

/// <summary>
/// A domain event persisted in the same transaction as the change that raised it. The outbox
/// processor reads it back after commit and invokes the event's handlers, at least once.
/// </summary>
public sealed class OutboxMessage {
    public const int MaxEventTypeLength = 200;
    public const int MaxErrorLength = 2000;

    private OutboxMessage() {
    }

    /// <summary>The domain event's own id, so the same event can never be stored twice.</summary>
    public Guid Id { get; private set; }

    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(Guid eventId, string eventType, string payload, DateTimeOffset occurredAt, DateTimeOffset now) =>
        new() {
            Id = eventId,
            EventType = eventType,
            Payload = payload,
            OccurredAt = occurredAt,
            CreatedAt = now,
            Status = OutboxMessageStatus.Pending,
            NextAttemptAt = now
        };

    public void MarkProcessed(DateTimeOffset now) {
        AttemptCount++;
        Status = OutboxMessageStatus.Processed;
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>Records a failed processing attempt; <paramref name="retryAt"/> <c>null</c> means give up.</summary>
    public void RecordFailure(string error, DateTimeOffset? retryAt, DateTimeOffset now) {
        AttemptCount++;
        LastError = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
        if (retryAt is { } next) {
            NextAttemptAt = next;
        } else {
            Status = OutboxMessageStatus.Failed;
            ProcessedAt = now;
        }
    }
}
