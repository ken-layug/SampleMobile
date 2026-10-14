using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Services;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Tests.Application;

/// <summary>Provider-level behaviour of the dispatcher, with substituted ports and providers.</summary>
public class NotificationDispatcherTests {
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly IDeliveryJobRepository _jobs = Substitute.For<IDeliveryJobRepository>();
    private readonly IDeviceRegistrationRepository _registrations = Substitute.For<IDeviceRegistrationRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly DeliveryOptions _options = new() {
        MaxAttempts = 3,
        RetryBaseDelay = TimeSpan.FromSeconds(10),
        RetryMaxDelay = TimeSpan.FromMinutes(1),
        SendTimeout = TimeSpan.FromSeconds(5)
    };

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_fcm_registration_is_never_sent_through_an_incompatible_provider() {
        var simulated = Provider("simulated", PushDispatchResult.Accepted("sim-1", isSimulated: true));
        var (job, _, claim) = ArrangeJob("fcm");

        var status = await Dispatcher(simulated).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.Failed, status);
        Assert.Equal("no_compatible_provider", job.LastErrorCode);
        Assert.Equal(DeliveryAttemptStatus.Skipped, job.Attempts.Single().Status);
        await simulated.DidNotReceiveWithAnyArgs().SendAsync(default!, Cancellation);
    }

    [Fact]
    public async Task Provider_acceptance_is_recorded_as_accepted_not_delivered() {
        var fcm = Provider("fcm", PushDispatchResult.Accepted("projects/demo/messages/1"));
        var (job, _, claim) = ArrangeJob("fcm");

        var status = await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.ProviderAccepted, status);
        Assert.Equal("projects/demo/messages/1", job.ProviderMessageId);
        Assert.Equal(DispatchOutcome.Accepted, job.Attempts.Single().Outcome);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_request_carries_the_registrations_current_address_and_only_ids_as_data() {
        var fcm = Provider("fcm", PushDispatchResult.Accepted("id"));
        var (job, registration, claim) = ArrangeJob("fcm");

        await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        await fcm.Received(1).SendAsync(
            Arg.Is<PushDispatchRequest>(request =>
                request.DeliveryJobId == job.Id
                && request.Target.Address == registration.Address
                && request.Target.ProviderId == "fcm"
                && request.Message.NotificationId == job.NotificationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_transient_failure_schedules_a_retry_with_backoff() {
        var fcm = Provider("fcm", PushDispatchResult.Failed(DispatchOutcome.TransientFailure, "fcm_unavailable", "try later"));
        var (job, registration, claim) = ArrangeJob("fcm");

        var status = await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.RetryScheduled, status);
        Assert.Equal(Start.AddSeconds(10), job.NextAttemptAt);
        Assert.True(registration.IsEnabled);

        _time.Advance(TimeSpan.FromSeconds(10));
        var secondClaim = new ClaimedDeliveryJob(job.Id, job.Claim(_time.GetUtcNow(), TimeSpan.FromMinutes(1)).AttemptNumber);
        await Dispatcher(fcm).DispatchAsync(secondClaim, Cancellation);

        Assert.Equal(_time.GetUtcNow().AddSeconds(20), job.NextAttemptAt);
    }

    [Fact]
    public async Task Transient_failures_stop_once_the_retry_budget_is_spent() {
        var fcm = Provider("fcm", PushDispatchResult.Failed(DispatchOutcome.TransientFailure, "fcm_unavailable", "try later"));
        var (job, _, claim) = ArrangeJob("fcm");
        var dispatcher = Dispatcher(fcm);

        await dispatcher.DispatchAsync(claim, Cancellation);
        for (var attempt = 2; attempt <= _options.MaxAttempts; attempt++) {
            _time.SetUtcNow(job.NextAttemptAt);
            var next = job.Claim(_time.GetUtcNow(), TimeSpan.FromMinutes(1));
            await dispatcher.DispatchAsync(new ClaimedDeliveryJob(job.Id, next.AttemptNumber), Cancellation);
        }

        Assert.Equal(DeliveryJobStatus.Failed, job.Status);
        Assert.Equal(_options.MaxAttempts, job.AttemptCount);
    }

    [Fact]
    public async Task A_longer_retry_after_from_the_provider_wins_over_the_backoff() {
        var fcm = Provider("fcm", PushDispatchResult.Failed(
            DispatchOutcome.TransientFailure, "fcm_quota_exceeded", "slow down", retryAfter: TimeSpan.FromMinutes(2)));
        var (job, _, claim) = ArrangeJob("fcm");

        await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(Start.AddMinutes(2), job.NextAttemptAt);
    }

    [Fact]
    public async Task A_permanent_failure_stops_retrying() {
        var fcm = Provider("fcm", PushDispatchResult.Failed(DispatchOutcome.PermanentFailure, "fcm_invalid_argument", "bad message"));
        var (job, registration, claim) = ArrangeJob("fcm");

        var status = await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.Failed, status);
        Assert.False(job.IsDue(Start.AddDays(1)));
        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public async Task An_invalid_registration_is_disabled() {
        var fcm = Provider("fcm", PushDispatchResult.Failed(DispatchOutcome.InvalidRegistration, "fcm_unregistered", "token expired"));
        var (job, registration, claim) = ArrangeJob("fcm");

        await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.Failed, job.Status);
        Assert.False(registration.IsEnabled);
        Assert.Equal(DeviceDisabledReason.InvalidatedByProvider, registration.DisabledReason);
    }

    [Fact]
    public async Task An_authentication_failure_never_disables_the_device() {
        var fcm = Provider("fcm", PushDispatchResult.Failed(DispatchOutcome.AuthenticationFailure, "firebase_not_configured", "no credentials"));
        var (job, registration, claim) = ArrangeJob("fcm");

        await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.Failed, job.Status);
        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public async Task A_provider_exception_is_treated_as_transient() {
        var fcm = Substitute.For<IPushNotificationProvider>();
        fcm.ProviderId.Returns("fcm");
        fcm.SendAsync(Arg.Any<PushDispatchRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("socket closed"));
        var (job, _, claim) = ArrangeJob("fcm");

        var status = await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.RetryScheduled, status);
        Assert.Equal("provider_exception", job.LastErrorCode);
    }

    [Fact]
    public async Task A_disabled_registration_cancels_the_job_without_sending() {
        var fcm = Provider("fcm", PushDispatchResult.Accepted("id"));
        var (job, registration, claim) = ArrangeJob("fcm");
        registration.Disable(DeviceDisabledReason.Unregistered, Start);

        var status = await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Equal(DeliveryJobStatus.Cancelled, status);
        await fcm.DidNotReceiveWithAnyArgs().SendAsync(default!, Cancellation);
    }

    [Fact]
    public async Task A_result_is_discarded_when_another_worker_took_over_the_job() {
        var fcm = Provider("fcm", PushDispatchResult.Accepted("id"));
        var (_, _, claim) = ArrangeJob("fcm");
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("changed", new Exception()));

        var status = await Dispatcher(fcm).DispatchAsync(claim, Cancellation);

        Assert.Null(status);
    }

    private NotificationDispatcher Dispatcher(params IPushNotificationProvider[] providers) =>
        new(
            _jobs,
            _registrations,
            _notifications,
            new PushProviderResolver(providers),
            _unitOfWork,
            Options.Create(_options),
            _time,
            NullLogger<NotificationDispatcher>.Instance);

    private (DeliveryJob Job, DeviceRegistration Registration, ClaimedDeliveryJob Claim) ArrangeJob(string providerId) {
        var userId = Guid.CreateVersion7();
        var notification = Notification.Create(
            NotificationType.Create("demo.message"), "Title", "Body", null, null, [userId], null, Start);
        var registration = DeviceRegistration.Register(
            userId, "installation-1", DevicePlatform.Android, providerId, "provider-token-0123456789", Start);
        var job = DeliveryJob.Create(notification.Id, userId, registration.Id, providerId, Start);
        var attempt = job.Claim(Start, TimeSpan.FromMinutes(1));

        _jobs.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _notifications.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);

        return (job, registration, new ClaimedDeliveryJob(job.Id, attempt.AttemptNumber));
    }

    private static IPushNotificationProvider Provider(string providerId, PushDispatchResult result) {
        var provider = Substitute.For<IPushNotificationProvider>();
        provider.ProviderId.Returns(providerId);
        provider.SendAsync(Arg.Any<PushDispatchRequest>(), Arg.Any<CancellationToken>()).Returns(result);
        return provider;
    }
}
