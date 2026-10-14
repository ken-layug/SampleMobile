using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;
using SampleMobile.Domain.Services;

namespace SampleMobile.Application.Services;

/// <summary>Sends one claimed delivery job through its provider and records what happened.</summary>
public interface INotificationDispatcher {
    /// <summary>
    /// Dispatches a job previously claimed by this worker. Returns the job's new status, or <c>null</c>
    /// when the job was no longer ours to finish (lease lost, already handled).
    /// </summary>
    Task<DeliveryJobStatus?> DispatchAsync(ClaimedDeliveryJob claim, CancellationToken cancellationToken = default);

    /// <summary>Hands a claimed job back without an outcome, e.g. on shutdown.</summary>
    Task ReleaseAsync(ClaimedDeliveryJob claim, string reason, CancellationToken cancellationToken = default);
}

public sealed class NotificationDispatcher(
    IDeliveryJobRepository deliveryJobs,
    IDeviceRegistrationRepository registrations,
    INotificationRepository notifications,
    IPushProviderResolver providers,
    IUnitOfWork unitOfWork,
    IOptions<DeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher {

    private readonly DeliveryRetryPolicy _retryPolicy = options.Value.CreateRetryPolicy();

    public async Task<DeliveryJobStatus?> DispatchAsync(ClaimedDeliveryJob claim, CancellationToken cancellationToken = default) {
        var job = await deliveryJobs.GetByIdAsync(claim.JobId, cancellationToken);
        if (job is null || !job.IsClaimedFor(claim.AttemptNumber)) {
            logger.LogWarning(
                "Delivery job {JobId} attempt {AttemptNumber} is no longer held by this worker; skipping",
                claim.JobId, claim.AttemptNumber);
            return null;
        }

        var registration = await registrations.GetByIdAsync(job.DeviceRegistrationId, cancellationToken);
        if (registration is not { IsEnabled: true }) {
            job.Cancel("registration_disabled", "The device registration was disabled before the message was sent.", timeProvider.GetUtcNow());
            return await SaveAsync(job);
        }

        // Defensive: provider ids are part of the registration's identity and never change, but a job
        // must never be sent through anything other than the provider its registration belongs to.
        if (!string.Equals(registration.ProviderId, job.ProviderId, StringComparison.Ordinal)
            || !providers.TryResolve(job.ProviderId, out var provider)) {
            job.FailWithoutSending(
                "no_compatible_provider",
                $"No push provider is registered for '{job.ProviderId}'. The registration cannot be sent through any other provider.",
                timeProvider.GetUtcNow());
            return await SaveAsync(job);
        }

        var notification = await notifications.GetByIdAsync(job.NotificationId, cancellationToken)
            ?? throw new InvalidOperationException($"Notification {job.NotificationId} for delivery job {job.Id} does not exist.");

        var request = new PushDispatchRequest(
            job.Id,
            claim.AttemptNumber,
            new PushTarget(registration.Id, registration.ProviderId, registration.Platform, registration.Address),
            new PushMessage(notification.Id, notification.Type.Value, notification.Title, notification.Body, notification.ResourceId));

        var result = await SendAsync(provider, request, registration, cancellationToken);
        Apply(job, registration, result, timeProvider.GetUtcNow());
        return await SaveAsync(job);
    }

    public async Task ReleaseAsync(ClaimedDeliveryJob claim, string reason, CancellationToken cancellationToken = default) {
        var job = await deliveryJobs.GetByIdAsync(claim.JobId, cancellationToken);
        if (job is null || !job.IsClaimedFor(claim.AttemptNumber)) {
            return;
        }

        job.Release(reason, timeProvider.GetUtcNow());
        await SaveAsync(job);
    }

    private async Task<PushDispatchResult> SendAsync(
        IPushNotificationProvider provider,
        PushDispatchRequest request,
        DeviceRegistration registration,
        CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.SendTimeout);

        try {
            return await provider.SendAsync(request, timeout.Token);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Shutdown, not a provider problem: let the worker release the job.
            throw;
        } catch (OperationCanceledException) {
            return PushDispatchResult.Failed(
                DispatchOutcome.TransientFailure,
                "provider_timeout",
                $"The provider did not respond within {options.Value.SendTimeout.TotalSeconds:0} seconds.");
        } catch (Exception exception) {
            logger.LogError(
                exception,
                "Provider {ProviderId} threw while sending job {JobId} to {Address}; treating as transient",
                provider.ProviderId, request.DeliveryJobId, TokenMasker.Mask(registration.Address));
            return PushDispatchResult.Failed(
                DispatchOutcome.TransientFailure,
                "provider_exception",
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void Apply(DeliveryJob job, DeviceRegistration registration, PushDispatchResult result, DateTimeOffset now) {
        switch (result.Outcome) {
            case DispatchOutcome.Accepted:
                job.RecordAccepted(result.ProviderMessageId, result.IsSimulated, now);
                break;

            case DispatchOutcome.TransientFailure:
                var delay = _retryPolicy.GetDelayAfter(job.AttemptsInCurrentRun);
                DateTimeOffset? retryAt = delay is { } backoff
                    ? now + (result.RetryAfter is { } retryAfter && retryAfter > backoff ? retryAfter : backoff)
                    : null;
                job.RecordFailure(result.Outcome, result.ErrorCode, result.ErrorMessage, result.IsSimulated, retryAt, now);
                break;

            case DispatchOutcome.InvalidRegistration:
                // Only an explicit "this address is invalid" from the provider disables a device;
                // configuration or auth errors never do, so a misconfigured project can't wipe out valid devices.
                job.RecordFailure(result.Outcome, result.ErrorCode, result.ErrorMessage, result.IsSimulated, retryAt: null, now);
                registration.Disable(DeviceDisabledReason.InvalidatedByProvider, now);
                break;

            case DispatchOutcome.AuthenticationFailure:
            case DispatchOutcome.PermanentFailure:
                job.RecordFailure(result.Outcome, result.ErrorCode, result.ErrorMessage, result.IsSimulated, retryAt: null, now);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unknown dispatch outcome.");
        }

        logger.LogInformation(
            "Delivery job {JobId} attempt {AttemptNumber} via {ProviderId} to {Address}: {Outcome} -> {Status}{Retry}",
            job.Id,
            job.AttemptCount,
            job.ProviderId,
            TokenMasker.Mask(registration.Address),
            result.Outcome,
            job.Status,
            job.Status == DeliveryJobStatus.RetryScheduled ? $" (next attempt at {job.NextAttemptAt:O})" : string.Empty);
    }

    private async Task<DeliveryJobStatus?> SaveAsync(DeliveryJob job) {
        try {
            // Not the caller's token: once a provider has answered, the outcome must be recorded even
            // if shutdown has begun, otherwise the job is retried and the device may get a duplicate.
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return job.Status;
        } catch (ConcurrencyConflictException) {
            logger.LogWarning(
                "Delivery job {JobId} was taken over by another worker after this worker's lease expired; discarding this result",
                job.Id);
            return null;
        }
    }
}
