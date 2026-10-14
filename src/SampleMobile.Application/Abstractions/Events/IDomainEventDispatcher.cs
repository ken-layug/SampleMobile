using SampleMobile.Domain.Primitives;

namespace SampleMobile.Application.Abstractions.Events;

/// <summary>Invokes every registered <see cref="IDomainEventHandler{TEvent}"/> for an event; failures propagate so the outbox can retry.</summary>
public interface IDomainEventDispatcher {
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
