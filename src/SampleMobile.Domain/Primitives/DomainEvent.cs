namespace SampleMobile.Domain.Primitives;

/// <summary>Base record for domain events; derive as <c>sealed record XHappened(...) : DomainEvent</c>.</summary>
public abstract record DomainEvent : IDomainEvent {
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
