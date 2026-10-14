using SampleMobile.Domain.Primitives;

namespace SampleMobile.Application.Abstractions.Events;

/// <summary>
/// Reacts to a domain event read back from the transactional outbox, after the transaction that
/// raised it has committed. Delivery is <b>at least once</b>: the same event can be handled again
/// after a crash or retry, so every handler must be idempotent (e.g. via a deduplication key).
/// A handler that throws makes the outbox retry the event later.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent {
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
