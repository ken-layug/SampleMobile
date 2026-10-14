namespace SampleMobile.Domain.Entities;

/// <summary>The normalized result of handing a message to a push provider.</summary>
public enum DispatchOutcome {
    /// <summary>The provider accepted the message. This is not proof the device displayed it.</summary>
    Accepted,

    /// <summary>A temporary problem (network, throttling, provider outage); retrying may succeed.</summary>
    TransientFailure,

    /// <summary>The provider explicitly confirmed the device address is no longer valid.</summary>
    InvalidRegistration,

    /// <summary>Credentials or provider configuration are missing or rejected; retrying won't help until fixed.</summary>
    AuthenticationFailure,

    /// <summary>The message can never be accepted as sent (malformed request, unsupported target).</summary>
    PermanentFailure
}

public enum DeliveryAttemptStatus {
    InProgress,
    Completed,

    /// <summary>The worker lost its lease or shut down before recording an outcome; the provider may or may not have accepted.</summary>
    Abandoned,

    /// <summary>The attempt ended before anything was sent to a provider.</summary>
    Skipped
}

/// <summary>One try at handing a delivery job to its provider. Every claim produces exactly one attempt row.</summary>
public sealed class DeliveryAttempt {
    public const int MaxErrorCodeLength = 100;
    public const int MaxErrorMessageLength = 1000;
    public const int MaxProviderMessageIdLength = 500;

    private DeliveryAttempt() {
    }

    public Guid Id { get; private set; }
    public Guid DeliveryJobId { get; private set; }
    public int AttemptNumber { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public DeliveryAttemptStatus Status { get; private set; }
    public DispatchOutcome? Outcome { get; private set; }
    public bool IsSimulated { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    internal static DeliveryAttempt Start(Guid deliveryJobId, int attemptNumber, string providerId, DateTimeOffset now) =>
        new() {
            Id = Guid.CreateVersion7(),
            DeliveryJobId = deliveryJobId,
            AttemptNumber = attemptNumber,
            ProviderId = providerId,
            Status = DeliveryAttemptStatus.InProgress,
            StartedAt = now
        };

    internal void Complete(
        DispatchOutcome outcome,
        string? providerMessageId,
        string? errorCode,
        string? errorMessage,
        bool isSimulated,
        DateTimeOffset now) {
        EnsureInProgress();
        Status = DeliveryAttemptStatus.Completed;
        Outcome = outcome;
        IsSimulated = isSimulated;
        ProviderMessageId = Truncate(providerMessageId, MaxProviderMessageIdLength);
        ErrorCode = Truncate(errorCode, MaxErrorCodeLength);
        ErrorMessage = Truncate(errorMessage, MaxErrorMessageLength);
        CompletedAt = now;
    }

    internal void Abandon(string reason, DateTimeOffset now) {
        EnsureInProgress();
        Status = DeliveryAttemptStatus.Abandoned;
        ErrorCode = "abandoned";
        ErrorMessage = Truncate(reason, MaxErrorMessageLength);
        CompletedAt = now;
    }

    internal void Skip(string errorCode, string reason, DateTimeOffset now) {
        EnsureInProgress();
        Status = DeliveryAttemptStatus.Skipped;
        ErrorCode = Truncate(errorCode, MaxErrorCodeLength);
        ErrorMessage = Truncate(reason, MaxErrorMessageLength);
        CompletedAt = now;
    }

    private void EnsureInProgress() {
        if (Status != DeliveryAttemptStatus.InProgress) {
            throw new InvalidOperationException($"Attempt {AttemptNumber} of job {DeliveryJobId} is already {Status}.");
        }
    }

    internal static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
