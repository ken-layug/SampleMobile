using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SampleMobile.Application.Abstractions.Events;
using SampleMobile.Domain.Primitives;

namespace SampleMobile.Infrastructure.Events;

/// <summary>
/// Invokes every <see cref="IDomainEventHandler{TEvent}"/> registered for an event's concrete type.
/// Unlike a best-effort post-commit dispatcher, failures propagate: the outbox processor rolls back,
/// records the error and retries the event later.
/// </summary>
public sealed class OutboxDomainEventDispatcher(
    IServiceProvider services,
    ILogger<OutboxDomainEventDispatcher> logger) : IDomainEventDispatcher {

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        var handlers = services.GetServices(handlerType).ToList();

        if (handlers.Count == 0) {
            logger.LogDebug("No handlers registered for {EventType} ({EventId})", domainEvent.GetType().Name, domainEvent.EventId);
            return;
        }

        foreach (var handler in handlers) {
            await (Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!;
        }
    }
}
