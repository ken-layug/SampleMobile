using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Services;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Persistence;
using SampleMobile.Infrastructure.Push;

namespace SampleMobile.Tests.Integration;

/// <summary>Shared steps for integration tests: arranging data through the real use cases and running the worker's steps by hand.</summary>
public abstract class IntegrationTestBase(IntegrationFixture fixture) {
    protected IntegrationFixture Fixture { get; } = fixture;

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected async Task<Guid> UserIdAsync(string userName) {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(user => user.UserName == userName).Select(user => user.Id).SingleAsync(Cancellation);
    }

    /// <summary>Registers a fresh virtual device on the simulated provider, optionally scripting its answers.</summary>
    protected async Task<DeviceRegistration> RegisterSimulatedDeviceAsync(Guid userId, params DispatchOutcome[] script) {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var address = $"sim-it-{Guid.CreateVersion7():N}";
        scope.ServiceProvider.GetRequiredService<ISimulatedPushControl>().ScriptOutcomes(address, script);

        var registration = DeviceRegistration.Register(
            userId, $"it-{Guid.CreateVersion7():N}", DevicePlatform.Virtual, PushProviderIds.Simulated, address, DateTimeOffset.UtcNow);
        db.DeviceRegistrations.Add(registration);
        await db.SaveChangesAsync(Cancellation);
        return registration;
    }

    protected async Task<PublishedNotification> PublishAsync(Guid recipientId, params Guid[] targetRegistrationIds) {
        await using var scope = Fixture.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(
            new NotificationIntent(
                "demo.message",
                $"Integration {Guid.CreateVersion7():N}",
                "Body",
                [recipientId],
                TargetRegistrationIds: targetRegistrationIds),
            Cancellation);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return result.Value!;
    }

    /// <summary>Claims due jobs like the worker does and dispatches the one we care about.</summary>
    protected async Task<DeliveryJobStatus?> ClaimAndDispatchAsync(Guid jobId) {
        IReadOnlyList<ClaimedDeliveryJob> claims;
        await using (var scope = Fixture.CreateScope()) {
            claims = await scope.ServiceProvider.GetRequiredService<IDeliveryJobRepository>()
                .ClaimDueJobsAsync(1000, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), Cancellation);
        }

        var claim = Assert.Single(claims, candidate => candidate.JobId == jobId);
        await using (var scope = Fixture.CreateScope()) {
            return await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().DispatchAsync(claim, Cancellation);
        }
    }
}
