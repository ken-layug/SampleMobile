using SampleMobile.Domain.Entities;
using SampleMobile.Domain.Events;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Tests.Infrastructure;

public class OutboxEventSerializerTests {
    [Fact]
    public void A_domain_event_survives_the_round_trip_through_an_outbox_row() {
        var domainEvent = new DemoAlertRaised(Guid.CreateVersion7(), Guid.CreateVersion7(), DemoAlertSeverity.Warning, "Disk usage high");
        var now = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

        var message = OutboxEventSerializer.ToOutboxMessage(domainEvent, now);

        Assert.Equal(domainEvent.EventId, message.Id);
        Assert.Equal(nameof(DemoAlertRaised), message.EventType);
        Assert.True(OutboxEventSerializer.TryDeserialize(message, out var restored));
        Assert.Equal(domainEvent, restored);
    }

    [Fact]
    public void Unknown_event_types_are_rejected_instead_of_resolved_by_name() {
        var message = OutboxMessage.Create(Guid.CreateVersion7(), "System.Diagnostics.Process", "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.False(OutboxEventSerializer.TryDeserialize(message, out _));
    }
}
