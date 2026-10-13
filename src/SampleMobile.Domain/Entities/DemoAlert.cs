using SampleMobile.Domain.Events;
using SampleMobile.Domain.Primitives;

namespace SampleMobile.Domain.Entities;

public enum DemoAlertSeverity {
    Info,
    Warning,
    Critical
}

/// <summary>
/// Sample business data standing in for "something in your domain happened". Saving an alert
/// raises <see cref="DemoAlertRaised"/>, which is written to the outbox in the same transaction;
/// turning it into a notification happens later and can never roll this row back.
/// </summary>
public sealed class DemoAlert : AggregateRoot {
    public const int MaxMessageLength = 500;

    private DemoAlert() {
    }

    public Guid Id { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public DemoAlertSeverity Severity { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset RaisedAt { get; private set; }

    public static DemoAlert Create(Guid recipientUserId, DemoAlertSeverity severity, string message, DateTimeOffset now) {
        if (recipientUserId == Guid.Empty) {
            throw new ArgumentException("An alert needs a recipient.", nameof(recipientUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Trim().Length, MaxMessageLength, nameof(message));

        var alert = new DemoAlert {
            Id = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            Severity = severity,
            Message = message.Trim(),
            RaisedAt = now
        };

        alert.Raise(new DemoAlertRaised(alert.Id, alert.RecipientUserId, alert.Severity, alert.Message) {
            OccurredOn = now
        });

        return alert;
    }
}
