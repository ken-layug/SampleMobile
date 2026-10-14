using SampleMobile.Domain.Primitives;

namespace SampleMobile.Domain.Entities;

public enum DeliveryJobStatus {
    /// <summary>Waiting for its first attempt.</summary>
    Pending,

    /// <summary>Claimed by a worker that holds a lease until <see cref="DeliveryJob.LockedUntil"/>.</summary>
    Processing,

    /// <summary>A transient failure occurred; the next attempt is due at <see cref="DeliveryJob.NextAttemptAt"/>.</summary>
    RetryScheduled,

    /// <summary>
    /// The provider accepted the message. Deliberately not called "Delivered": acceptance by FCM
    /// (or any provider) says nothing about whether the device received or displayed it.
    /// </summary>
    ProviderAccepted,

    /// <summary>Stopped: a permanent, configuration or invalid-registration failure, or retries were exhausted.</summary>
    Failed,

    /// <summary>Stopped before sending because the target registration is no longer usable.</summary>
    Cancelled
}

/// <summary>
/// The intent to push one notification to one device registration, plus its attempt history.
/// Jobs are processed at least once: a crash between provider acceptance and the database
/// commit can lead to a second send, which is why the attempt that was in flight is recorded
/// as <see cref="DeliveryAttemptStatus.Abandoned"/> rather than silently replaced.
/// </summary>
public sealed class DeliveryJob : AggregateRoot {
    private const string LeaseExpiredReason =
        "The worker's lease expired before an outcome was recorded. The provider may or may not have accepted the message.";

    private readonly List<DeliveryAttempt> _attempts = [];

    private DeliveryJob() {
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public Guid DeviceRegistrationId { get; private set; }

    /// <summary>The provider this job must be sent through: always the registration's own provider.</summary>
    public string ProviderId { get; private set; } = string.Empty;

    public DeliveryJobStatus Status { get; private set; }

    /// <summary>Total attempts over the job's lifetime; attempt numbers are never reused.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Attempts since the job was created or last requeued; drives the retry budget.</summary>
    public int AttemptsInCurrentRun { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DispatchOutcome? LastOutcome { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public bool IsSimulated { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public IReadOnlyCollection<DeliveryAttempt> Attempts => _attempts;

    public bool IsTerminal => Status is DeliveryJobStatus.ProviderAccepted or DeliveryJobStatus.Failed or DeliveryJobStatus.Cancelled;

    public bool CanRequeue => Status is DeliveryJobStatus.Failed or DeliveryJobStatus.Cancelled;

    public static DeliveryJob Create(
        Guid notificationId,
        Guid recipientUserId,
        Guid deviceRegistrationId,
        string providerId,
        DateTimeOffset now) {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        return new DeliveryJob {
            Id = Guid.CreateVersion7(),
            NotificationId = notificationId,
            RecipientUserId = recipientUserId,
            DeviceRegistrationId = deviceRegistrationId,
            ProviderId = DeviceRegistration.NormalizeProviderId(providerId),
            Status = DeliveryJobStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>A job is due when its next attempt time has passed, or when a worker's lease on it expired.</summary>
    public bool IsDue(DateTimeOffset now) =>
        Status switch {
            DeliveryJobStatus.Pending or DeliveryJobStatus.RetryScheduled => NextAttemptAt <= now,
            DeliveryJobStatus.Processing => LockedUntil is { } lockedUntil && lockedUntil <= now,
            _ => false
        };

    public DeliveryAttempt? CurrentAttempt =>
        _attempts.FirstOrDefault(attempt => attempt.Status == DeliveryAttemptStatus.InProgress);

    /// <summary>True when this job is still held by the worker that started <paramref name="attemptNumber"/>.</summary>
    public bool IsClaimedFor(int attemptNumber) =>
        Status == DeliveryJobStatus.Processing && AttemptCount == attemptNumber;

    /// <summary>
    /// Leases the job to a worker and opens a new attempt. Requires the attempts collection to be loaded
    /// so an attempt left in flight by a crashed worker can be marked abandoned.
    /// </summary>
    public DeliveryAttempt Claim(DateTimeOffset now, TimeSpan lease) {
        if (!IsDue(now)) {
            throw new InvalidOperationException($"Delivery job {Id} is not due (status {Status}).");
        }

        CurrentAttempt?.Abandon(LeaseExpiredReason, now);

        AttemptCount++;
        AttemptsInCurrentRun++;
        Status = DeliveryJobStatus.Processing;
        LockedUntil = now + lease;
        LastAttemptAt = now;
        UpdatedAt = now;

        var attempt = DeliveryAttempt.Start(Id, AttemptCount, ProviderId, now);
        _attempts.Add(attempt);
        return attempt;
    }

    public void RecordAccepted(string? providerMessageId, bool isSimulated, DateTimeOffset now) {
        RequireCurrentAttempt().Complete(DispatchOutcome.Accepted, providerMessageId, null, null, isSimulated, now);

        Status = DeliveryJobStatus.ProviderAccepted;
        ProviderMessageId = DeliveryAttempt.Truncate(providerMessageId, DeliveryAttempt.MaxProviderMessageIdLength);
        IsSimulated = isSimulated;
        LastOutcome = DispatchOutcome.Accepted;
        LastErrorCode = null;
        LastErrorMessage = null;
        LockedUntil = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a failed send. Pass <paramref name="retryAt"/> to schedule another attempt;
    /// leave it <c>null</c> to stop retrying.
    /// </summary>
    public void RecordFailure(
        DispatchOutcome outcome,
        string? errorCode,
        string? errorMessage,
        bool isSimulated,
        DateTimeOffset? retryAt,
        DateTimeOffset now) {
        if (outcome == DispatchOutcome.Accepted) {
            throw new ArgumentException("Use RecordAccepted for accepted messages.", nameof(outcome));
        }

        RequireCurrentAttempt().Complete(outcome, null, errorCode, errorMessage, isSimulated, now);

        LastOutcome = outcome;
        LastErrorCode = DeliveryAttempt.Truncate(errorCode, DeliveryAttempt.MaxErrorCodeLength);
        LastErrorMessage = DeliveryAttempt.Truncate(errorMessage, DeliveryAttempt.MaxErrorMessageLength);
        IsSimulated = isSimulated;
        LockedUntil = null;
        UpdatedAt = now;

        if (retryAt is { } nextAttemptAt) {
            Status = DeliveryJobStatus.RetryScheduled;
            NextAttemptAt = nextAttemptAt;
        } else {
            Status = DeliveryJobStatus.Failed;
            CompletedAt = now;
        }
    }

    /// <summary>Fails the job without contacting any provider, e.g. when no compatible provider is registered.</summary>
    public void FailWithoutSending(string errorCode, string reason, DateTimeOffset now) {
        CurrentAttempt?.Skip(errorCode, reason, now);

        Status = DeliveryJobStatus.Failed;
        LastOutcome = DispatchOutcome.PermanentFailure;
        LastErrorCode = errorCode;
        LastErrorMessage = DeliveryAttempt.Truncate(reason, DeliveryAttempt.MaxErrorMessageLength);
        LockedUntil = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Stops the job before sending because its target can no longer receive pushes.</summary>
    public void Cancel(string errorCode, string reason, DateTimeOffset now) {
        if (IsTerminal) {
            return;
        }

        CurrentAttempt?.Skip(errorCode, reason, now);

        Status = DeliveryJobStatus.Cancelled;
        LastErrorCode = errorCode;
        LastErrorMessage = DeliveryAttempt.Truncate(reason, DeliveryAttempt.MaxErrorMessageLength);
        LockedUntil = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Gives a claimed job back without an outcome (graceful shutdown). The in-flight attempt is
    /// recorded as abandoned and the job becomes due immediately.
    /// </summary>
    public void Release(string reason, DateTimeOffset now) {
        if (Status != DeliveryJobStatus.Processing) {
            return;
        }

        CurrentAttempt?.Abandon(reason, now);
        Status = DeliveryJobStatus.RetryScheduled;
        NextAttemptAt = now;
        LockedUntil = null;
        UpdatedAt = now;
    }

    /// <summary>Puts a stopped job back in the queue with a fresh retry budget (e.g. after fixing provider credentials).</summary>
    public void Requeue(DateTimeOffset now) {
        if (!CanRequeue) {
            throw new InvalidOperationException($"Delivery job {Id} cannot be requeued from status {Status}.");
        }

        Status = DeliveryJobStatus.Pending;
        AttemptsInCurrentRun = 0;
        NextAttemptAt = now;
        CompletedAt = null;
        UpdatedAt = now;
    }

    private DeliveryAttempt RequireCurrentAttempt() =>
        CurrentAttempt ?? throw new InvalidOperationException($"Delivery job {Id} has no attempt in progress.");
}
