using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Services;

namespace SampleMobile.Infrastructure.BackgroundServices;

/// <summary>
/// Claims due delivery jobs and dispatches them through their providers.
///
/// Wake-ups come from PostgreSQL <c>NOTIFY</c> (new or requeued jobs) or from a timer set to the next
/// retry time, capped by <see cref="DeliveryOptions.MaxIdleWait"/> — there is no fixed-interval polling.
/// Claims use <c>FOR UPDATE SKIP LOCKED</c> plus a lease, so several workers (or app instances) never
/// send the same job concurrently. Delivery is still at least once: if the process dies after the
/// provider accepted a message but before the outcome was saved, the lease expires and the job is
/// sent again. The abandoned attempt stays visible in the history.
/// </summary>
public sealed class DeliveryWorker(
    IServiceScopeFactory scopeFactory,
    WorkSignal signal,
    TimeProvider timeProvider,
    IOptions<DeliveryOptions> options,
    ILogger<DeliveryWorker> logger) : BackgroundService {

    private const string ShutdownReason =
        "The worker shut down before the provider answered. The provider may or may not have accepted the message.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!options.Value.WorkerEnabled) {
            logger.LogInformation("Delivery worker is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var claims = await ClaimAsync(stoppingToken);
                if (claims.Count > 0) {
                    // CancellationToken.None: every claimed job must be either dispatched or explicitly
                    // released, even when shutdown starts halfway through the batch.
                    await Parallel.ForEachAsync(
                        claims,
                        new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.MaxParallelism) },
                        async (claim, _) => await DispatchOneAsync(claim, stoppingToken));
                    continue;
                }

                await signal.WaitAsync(WorkChannels.Delivery, await GetIdleWaitAsync(stoppingToken), stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogError(exception, "Delivery loop failed; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<IReadOnlyList<ClaimedDeliveryJob>> ClaimAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IDeliveryJobRepository>();
        return await jobs.ClaimDueJobsAsync(
            options.Value.BatchSize, timeProvider.GetUtcNow(), options.Value.LeaseDuration, cancellationToken);
    }

    private async Task DispatchOneAsync(ClaimedDeliveryJob claim, CancellationToken stoppingToken) {
        try {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().DispatchAsync(claim, stoppingToken);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            await ReleaseAsync(claim);
        } catch (Exception exception) {
            logger.LogError(
                exception,
                "Dispatching delivery job {JobId} failed unexpectedly; it will be retried when its lease expires",
                claim.JobId);
        }
    }

    private async Task ReleaseAsync(ClaimedDeliveryJob claim) {
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>()
                .ReleaseAsync(claim, ShutdownReason, timeout.Token);
            logger.LogInformation("Released delivery job {JobId} on shutdown", claim.JobId);
        } catch (Exception exception) {
            logger.LogWarning(exception, "Could not release delivery job {JobId}; it will be retried when its lease expires", claim.JobId);
        }
    }

    private async Task<TimeSpan> GetIdleWaitAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var nextDue = await scope.ServiceProvider.GetRequiredService<IDeliveryJobRepository>().GetNextDueAtAsync(cancellationToken);
        return WaitPolicy.Until(nextDue, timeProvider.GetUtcNow(), options.Value.MaxIdleWait);
    }
}
