using SampleMobile.Application.Abstractions.Events;
using SampleMobile.Application.Common;
using SampleMobile.Application.Services;
using SampleMobile.Domain.Events;

namespace SampleMobile.Application.Events.Handlers;

/// <summary>
/// Translates a <see cref="DemoAlertRaised"/> domain event into a notification intent.
/// Runs from the outbox, possibly more than once for the same event; the deduplication key
/// derived from the event id makes repeats a no-op.
/// </summary>
public sealed class DemoAlertNotificationHandler(INotificationPublisher publisher) : IDomainEventHandler<DemoAlertRaised> {
    public static string DeduplicationKeyFor(DemoAlertRaised domainEvent) => $"demo-alert-raised:{domainEvent.EventId:N}";

    public static NotificationIntent CreateIntent(DemoAlertRaised domainEvent) =>
        new(
            Type: NotificationTypes.DemoAlert,
            Title: $"{domainEvent.Severity} alert",
            Body: domainEvent.Message,
            RecipientUserIds: [domainEvent.RecipientUserId],
            Metadata: new Dictionary<string, string> {
                ["severity"] = domainEvent.Severity.ToString().ToLowerInvariant(),
                ["source"] = "demo-alert"
            },
            ResourceId: domainEvent.AlertId.ToString(),
            DeduplicationKey: DeduplicationKeyFor(domainEvent));

    public async Task HandleAsync(DemoAlertRaised domainEvent, CancellationToken cancellationToken = default) {
        var result = await publisher.PublishAsync(CreateIntent(domainEvent), cancellationToken);
        if (!result.Succeeded) {
            // Throwing hands the event back to the outbox, which records the error and retries with backoff.
            var detail = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
            throw new InvalidOperationException($"Could not create a notification for demo alert {domainEvent.AlertId}: {detail}");
        }
    }
}
