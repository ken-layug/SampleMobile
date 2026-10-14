using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Abstractions.Push;

/// <summary>
/// Test controls for the simulated provider: script failures for an address and read back what
/// the simulator "sent". Nothing here involves a real device or network.
/// </summary>
public interface ISimulatedPushControl {
    /// <summary>The provider id virtual devices must be registered with.</summary>
    string ProviderId { get; }

    /// <summary>
    /// Queues outcomes for the next dispatches to <paramref name="address"/>, consumed in order.
    /// Once the queue is empty the simulator accepts every message.
    /// </summary>
    void ScriptOutcomes(string address, IReadOnlyList<DispatchOutcome> outcomes);

    /// <summary>The most recent simulated dispatches, newest first. Held in memory only.</summary>
    IReadOnlyList<SimulatedPushRecord> GetRecentMessages(int maxCount);
}

/// <summary>A message the simulator handled. Addresses are masked.</summary>
public sealed record SimulatedPushRecord(
    string SimulatedMessageId,
    DateTimeOffset HandledAt,
    Guid DeliveryJobId,
    int AttemptNumber,
    Guid NotificationId,
    string NotificationType,
    string Title,
    string MaskedAddress,
    DispatchOutcome Outcome,
    string? ErrorCode);
