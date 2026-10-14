using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Events.Handlers;
using SampleMobile.Application.Features.DemoAlerts;
using SampleMobile.Domain.Entities;
using SampleMobile.Domain.Events;
using SampleMobile.Infrastructure.BackgroundServices;
using SampleMobile.Infrastructure.Persistence;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Tests.Integration;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class DeliveryPipelineTests(IntegrationFixture fixture) : IntegrationTestBase(fixture) {
    [Fact]
    public async Task An_outbox_event_processed_twice_creates_exactly_one_notification() {
        var bobId = await UserIdAsync(IntegrationFixture.BobUserName);
        RaisedDemoAlert raised;
        await using (var scope = Fixture.CreateScope()) {
            var result = await scope.ServiceProvider.GetRequiredService<ISender>()
                .SendAsync(new RaiseDemoAlertCommand(bobId, DemoAlertSeverity.Warning, "Outbox idempotency"), Cancellation);
            raised = result.Value!;
        }

        await DrainOutboxAsync();
        await ResetOutboxMessageAsync(raised.EventId);
        await DrainOutboxAsync();

        await using var verify = Fixture.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = DemoAlertNotificationHandler.DeduplicationKeyFor(new DemoAlertRaised(raised.AlertId, bobId, DemoAlertSeverity.Warning, "x") {
            EventId = raised.EventId
        });
        Assert.Equal(1, await db.Notifications.CountAsync(notification => notification.DeduplicationKey == key, Cancellation));
        var message = await db.OutboxMessages.SingleAsync(candidate => candidate.Id == raised.EventId, Cancellation);
        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        Assert.Equal(2, message.AttemptCount);
        Assert.True(await db.DemoAlerts.AnyAsync(alert => alert.Id == raised.AlertId, Cancellation));
    }

    [Fact]
    public async Task Concurrent_claims_never_hand_the_same_job_to_two_workers() {
        var aliceId = await UserIdAsync(IntegrationFixture.AliceUserName);
        var device = await RegisterSimulatedDeviceAsync(aliceId);
        var ownJobs = new List<Guid>();
        for (var i = 0; i < 12; i++) {
            ownJobs.AddRange((await PublishAsync(aliceId, device.Id)).DeliveryJobIds);
        }

        var claims = await Task.WhenAll(ClaimInOwnScopeAsync(), ClaimInOwnScopeAsync(), ClaimInOwnScopeAsync());

        var claimedIds = claims.SelectMany(batch => batch.Select(claim => claim.JobId)).ToList();
        Assert.Equal(claimedIds.Count, claimedIds.Distinct().Count());
        Assert.All(ownJobs, jobId => Assert.Contains(jobId, claimedIds));
    }

    [Fact]
    public async Task A_transient_failure_leaves_a_retryable_job_that_is_not_due_yet() {
        var aliceId = await UserIdAsync(IntegrationFixture.AliceUserName);
        var device = await RegisterSimulatedDeviceAsync(aliceId, DispatchOutcome.TransientFailure);
        var jobId = (await PublishAsync(aliceId, device.Id)).DeliveryJobIds.Single();

        var status = await ClaimAndDispatchAsync(jobId);

        Assert.Equal(DeliveryJobStatus.RetryScheduled, status);
        var immediate = await ClaimInOwnScopeAsync();
        Assert.DoesNotContain(immediate, claim => claim.JobId == jobId);
        await using var scope = Fixture.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryJobs.SingleAsync(candidate => candidate.Id == jobId, Cancellation);
        Assert.True(job.NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_permanent_failure_stops_and_the_notification_stays_in_the_inbox() {
        var aliceId = await UserIdAsync(IntegrationFixture.AliceUserName);
        var device = await RegisterSimulatedDeviceAsync(aliceId, DispatchOutcome.PermanentFailure);
        var published = await PublishAsync(aliceId, device.Id);

        var status = await ClaimAndDispatchAsync(published.DeliveryJobIds.Single());

        Assert.Equal(DeliveryJobStatus.Failed, status);
        var alice = await Fixture.CreateAliceClientAsync();
        var notification = await alice.GetFromJsonAsync<NotificationResponse>(
            $"/api/mobile/notifications/{published.NotificationId}", IntegrationFixture.Json, Cancellation);
        Assert.Equal(published.NotificationId, notification!.Id);
    }

    [Fact]
    public async Task An_unconfigured_fcm_provider_fails_the_job_but_keeps_the_notification_and_the_device() {
        var alice = await Fixture.CreateAliceClientAsync();
        var aliceId = await UserIdAsync(IntegrationFixture.AliceUserName);
        var installationId = Guid.NewGuid().ToString();
        var registration = await (await alice.PostAsJsonAsync("/api/mobile/device-registrations",
                new { installationId, platform = "android", provider = "fcm", address = $"fcm-token-{installationId}" }, IntegrationFixture.Json, Cancellation))
            .Content.ReadFromJsonAsync<DeviceRegistrationResponse>(IntegrationFixture.Json, Cancellation);
        var published = await PublishAsync(aliceId, registration!.Id);

        var status = await ClaimAndDispatchAsync(published.DeliveryJobIds.Single());

        Assert.Equal(DeliveryJobStatus.Failed, status);
        await using var scope = Fixture.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IDeliveryJobRepository>()
            .GetDetailAsync(published.DeliveryJobIds.Single(), Cancellation);
        Assert.Equal("firebase_not_configured", detail!.Job.LastErrorCode);
        Assert.True(detail.RegistrationEnabled);
        var inboxItem = await alice.GetAsync($"/api/mobile/notifications/{published.NotificationId}", Cancellation);
        Assert.True(inboxItem.IsSuccessStatusCode);
    }

    [Fact]
    public async Task An_invalid_registration_reported_by_the_provider_is_disabled() {
        var aliceId = await UserIdAsync(IntegrationFixture.AliceUserName);
        var device = await RegisterSimulatedDeviceAsync(aliceId, DispatchOutcome.InvalidRegistration);
        var jobId = (await PublishAsync(aliceId, device.Id)).DeliveryJobIds.Single();

        await ClaimAndDispatchAsync(jobId);

        await using var scope = Fixture.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeviceRegistrations
            .SingleAsync(registration => registration.Id == device.Id, Cancellation);
        Assert.False(stored.IsEnabled);
        Assert.Equal(DeviceDisabledReason.InvalidatedByProvider, stored.DisabledReason);
    }

    private async Task<IReadOnlyList<ClaimedDeliveryJob>> ClaimInOwnScopeAsync() {
        await using var scope = Fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDeliveryJobRepository>()
            .ClaimDueJobsAsync(1000, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), Cancellation);
    }

    private async Task DrainOutboxAsync() {
        var processor = Fixture.Factory.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        while (await processor.ProcessNextAsync(Cancellation)) {
        }
    }

    private async Task ResetOutboxMessageAsync(Guid messageId) {
        await using var scope = Fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .Where(message => message.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, OutboxMessageStatus.Pending)
                .SetProperty(message => message.ProcessedAt, (DateTimeOffset?)null), Cancellation);
    }
}
