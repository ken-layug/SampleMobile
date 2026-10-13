namespace SampleMobile.Domain.Primitives;

/// <summary>
/// Base class for aggregate roots. Mutation methods call <see cref="Raise"/> to queue domain
/// events; the unit of work persists them to the outbox atomically with the aggregate.
/// </summary>
public abstract class AggregateRoot : IHasDomainEvents {
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents() {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
