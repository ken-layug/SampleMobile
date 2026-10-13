namespace SampleMobile.Domain.Primitives;

/// <summary>
/// Implemented by aggregate roots that queue domain events to be written to the outbox
/// in the same transaction as the state change that raised them.
/// </summary>
public interface IHasDomainEvents {
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Returns the queued events and clears the queue.</summary>
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
}
