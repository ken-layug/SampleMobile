using NSubstitute;
using SampleMobile.Application.Common;
using SampleMobile.Application.Events.Handlers;
using SampleMobile.Application.Services;
using SampleMobile.Domain.Entities;
using SampleMobile.Domain.Events;

namespace SampleMobile.Tests.Application;

public class DemoAlertNotificationHandlerTests {
    [Fact]
    public async Task DemoAlertRaised_becomes_a_demo_alert_notification_for_the_recipient() {
        var publisher = Substitute.For<INotificationPublisher>();
        publisher.PublishAsync(Arg.Any<NotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PublishedNotification>.Success(new PublishedNotification(Guid.CreateVersion7(), [], false)));
        var domainEvent = new DemoAlertRaised(Guid.CreateVersion7(), Guid.CreateVersion7(), DemoAlertSeverity.Critical, "Disk usage above 90%");

        await new DemoAlertNotificationHandler(publisher).HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationIntent>(intent =>
                intent.Type == NotificationTypes.DemoAlert
                && intent.Title == "Critical alert"
                && intent.Body == "Disk usage above 90%"
                && intent.RecipientUserIds.Single() == domainEvent.RecipientUserId
                && intent.ResourceId == domainEvent.AlertId.ToString()
                && intent.Metadata!["severity"] == "critical"
                && intent.TargetRegistrationIds == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_deduplication_key_is_derived_from_the_event_id_so_replays_are_recognized() {
        var publisher = Substitute.For<INotificationPublisher>();
        publisher.PublishAsync(Arg.Any<NotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PublishedNotification>.Success(new PublishedNotification(Guid.CreateVersion7(), [], false)));
        var domainEvent = new DemoAlertRaised(Guid.CreateVersion7(), Guid.CreateVersion7(), DemoAlertSeverity.Info, "hello");
        var handler = new DemoAlertNotificationHandler(publisher);

        await handler.HandleAsync(domainEvent, TestContext.Current.CancellationToken);
        await handler.HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        var keys = publisher.ReceivedCalls()
            .Select(call => ((NotificationIntent)call.GetArguments()[0]!).DeduplicationKey)
            .Distinct()
            .ToList();
        Assert.Equal([$"demo-alert-raised:{domainEvent.EventId:N}"], keys);
    }

    [Fact]
    public async Task A_rejected_intent_throws_so_the_outbox_retries_the_event() {
        var publisher = Substitute.For<INotificationPublisher>();
        publisher.PublishAsync(Arg.Any<NotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PublishedNotification>.Invalid("recipientUserIds", "User does not exist."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoAlertNotificationHandler(publisher).HandleAsync(
            new DemoAlertRaised(Guid.CreateVersion7(), Guid.CreateVersion7(), DemoAlertSeverity.Info, "hello"),
            TestContext.Current.CancellationToken));
    }
}
