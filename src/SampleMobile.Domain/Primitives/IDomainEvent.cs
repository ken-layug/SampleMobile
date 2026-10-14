namespace SampleMobile.Domain.Primitives;

/// <summary>
/// A fact that happened in the domain, named in the past tense. Events are immutable and
/// carry their own identity + timestamp so they can be persisted to the outbox as-is.
/// </summary>
public interface IDomainEvent {
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}
