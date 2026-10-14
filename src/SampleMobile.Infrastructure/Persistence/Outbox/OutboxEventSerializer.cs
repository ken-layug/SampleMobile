using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using SampleMobile.Domain.Primitives;

namespace SampleMobile.Infrastructure.Persistence.Outbox;

/// <summary>
/// Converts domain events to and from outbox rows. The event type name stored in the database is
/// only ever resolved against concrete <see cref="IDomainEvent"/> types in the Domain assembly,
/// never via <c>Type.GetType</c> on database text.
/// </summary>
public static class OutboxEventSerializer {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyDictionary<string, Type> EventTypes = typeof(DomainEvent).Assembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
        .ToDictionary(type => type.Name, StringComparer.Ordinal);

    public static OutboxMessage ToOutboxMessage(IDomainEvent domainEvent, DateTimeOffset now) {
        var eventType = domainEvent.GetType().Name;
        if (!EventTypes.ContainsKey(eventType)) {
            throw new InvalidOperationException($"Domain event type {domainEvent.GetType().FullName} must live in the Domain assembly.");
        }

        var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions);
        return OutboxMessage.Create(domainEvent.EventId, eventType, payload, domainEvent.OccurredOn, now);
    }

    public static bool TryDeserialize(OutboxMessage message, [NotNullWhen(true)] out IDomainEvent? domainEvent) {
        domainEvent = null;
        if (!EventTypes.TryGetValue(message.EventType, out var type)) {
            return false;
        }

        domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(message.Payload, type, JsonOptions);
        return domainEvent is not null;
    }
}
