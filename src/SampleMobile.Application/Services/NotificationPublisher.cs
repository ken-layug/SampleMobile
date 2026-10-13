using Microsoft.Extensions.Logging;
using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Services;

/// <summary>
/// What should be told to whom. Built by use cases (dashboard compose) and by domain event handlers alike.
/// </summary>
/// <param name="TargetRegistrationIds">
/// <c>null</c>: push to every enabled registration of the recipients whose provider is available.
/// Empty: inbox only, no push. Otherwise: exactly these registrations, each of which must be usable.
/// </param>
/// <param name="DeduplicationKey">Identifies the business fact; publishing the same key twice returns the first notification.</param>
public sealed record NotificationIntent(
    string Type,
    string Title,
    string Body,
    IReadOnlyCollection<Guid> RecipientUserIds,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? ResourceId = null,
    string? DeduplicationKey = null,
    IReadOnlyCollection<Guid>? TargetRegistrationIds = null);

public sealed record PublishedNotification(Guid NotificationId, IReadOnlyList<Guid> DeliveryJobIds, bool WasDuplicate);

/// <summary>
/// Persists a notification and its delivery jobs. The single entry point both the manual and the
/// event-driven paths use, so neither knows anything about push providers beyond "is one available".
/// </summary>
public interface INotificationPublisher {
    Task<OperationResult<PublishedNotification>> PublishAsync(
        NotificationIntent intent, CancellationToken cancellationToken = default);
}

public sealed class NotificationPublisher(
    INotificationRepository notifications,
    IDeviceRegistrationRepository registrations,
    IDeliveryJobRepository deliveryJobs,
    IIdentityService identity,
    IPushProviderResolver providers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<NotificationPublisher> logger) : INotificationPublisher {

    public async Task<OperationResult<PublishedNotification>> PublishAsync(
        NotificationIntent intent, CancellationToken cancellationToken = default) {
        if (intent.DeduplicationKey is not null) {
            var existing = await notifications.GetByDeduplicationKeyAsync(intent.DeduplicationKey, cancellationToken);
            if (existing is not null) {
                logger.LogInformation(
                    "Notification for {DeduplicationKey} already exists as {NotificationId}; skipping duplicate",
                    intent.DeduplicationKey, existing.Id);
                return OperationResult<PublishedNotification>.Success(new PublishedNotification(existing.Id, [], WasDuplicate: true));
            }
        }

        var errors = ValidateContent(intent);
        var recipientIds = intent.RecipientUserIds.Distinct().ToList();
        if (recipientIds.Count == 0) {
            errors.Add(new OperationError("recipientUserIds", "At least one recipient is required."));
        }

        foreach (var userId in recipientIds) {
            if (await identity.FindByIdAsync(userId, cancellationToken) is null) {
                errors.Add(new OperationError("recipientUserIds", $"User {userId} does not exist."));
            }
        }

        if (errors.Count > 0) {
            return OperationResult<PublishedNotification>.Invalid(errors);
        }

        var targets = await SelectTargetsAsync(intent, recipientIds, cancellationToken);
        if (!targets.Succeeded) {
            return OperationResult<PublishedNotification>.FromFailure(targets);
        }

        var now = timeProvider.GetUtcNow();
        var notification = Notification.Create(
            NotificationType.Create(intent.Type),
            intent.Title,
            intent.Body,
            intent.Metadata,
            intent.ResourceId,
            recipientIds,
            intent.DeduplicationKey,
            now);

        // The notification and its jobs commit together. Nothing is sent here: the delivery worker
        // picks the jobs up afterwards, so a provider outage can never undo the notification.
        var jobs = targets.Value!
            .Select(registration => DeliveryJob.Create(notification.Id, registration.UserId, registration.Id, registration.ProviderId, now))
            .ToList();

        notifications.Add(notification);
        deliveryJobs.AddRange(jobs);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created notification {NotificationId} ({NotificationType}) for {RecipientCount} recipient(s) with {JobCount} delivery job(s)",
            notification.Id, notification.Type.Value, recipientIds.Count, jobs.Count);

        return OperationResult<PublishedNotification>.Success(
            new PublishedNotification(notification.Id, jobs.Select(job => job.Id).ToList(), WasDuplicate: false));
    }

    private async Task<OperationResult<IReadOnlyList<DeviceRegistration>>> SelectTargetsAsync(
        NotificationIntent intent, IReadOnlyCollection<Guid> recipientIds, CancellationToken cancellationToken) {
        if (intent.TargetRegistrationIds is null) {
            var enabled = await registrations.ListForUsersAsync(recipientIds, enabledOnly: true, cancellationToken);
            var compatible = new List<DeviceRegistration>();
            foreach (var registration in enabled) {
                if (providers.IsSupported(registration.ProviderId)) {
                    compatible.Add(registration);
                } else {
                    logger.LogWarning(
                        "Skipping device registration {RegistrationId}: no push provider is registered for '{ProviderId}'",
                        registration.Id, registration.ProviderId);
                }
            }

            return OperationResult<IReadOnlyList<DeviceRegistration>>.Success(compatible);
        }

        var requestedIds = intent.TargetRegistrationIds.Distinct().ToList();
        if (requestedIds.Count == 0) {
            return OperationResult<IReadOnlyList<DeviceRegistration>>.Success([]);
        }

        var found = await registrations.GetByIdsAsync(requestedIds, cancellationToken);
        var errors = new List<OperationError>();
        foreach (var registrationId in requestedIds) {
            var registration = found.FirstOrDefault(candidate => candidate.Id == registrationId);
            var problem = registration switch {
                null => "does not exist",
                _ when !recipientIds.Contains(registration.UserId) => "does not belong to a recipient",
                { IsEnabled: false } => "is disabled",
                _ when !providers.IsSupported(registration.ProviderId) =>
                    $"uses provider '{registration.ProviderId}', which has no registered adapter",
                _ => null
            };

            if (problem is not null) {
                errors.Add(new OperationError("targetRegistrationIds", $"Device registration {registrationId} {problem}."));
            }
        }

        return errors.Count > 0
            ? OperationResult<IReadOnlyList<DeviceRegistration>>.Invalid(errors)
            : OperationResult<IReadOnlyList<DeviceRegistration>>.Success(found);
    }

    private static List<OperationError> ValidateContent(NotificationIntent intent) {
        var errors = new List<OperationError>();
        if (!NotificationType.TryCreate(intent.Type, out _)) {
            errors.Add(new OperationError("type", "Type must be lower-case dot-separated segments, e.g. 'demo.alert'."));
        }

        if (string.IsNullOrWhiteSpace(intent.Title)) {
            errors.Add(new OperationError("title", "Title is required."));
        } else if (intent.Title.Trim().Length > Notification.MaxTitleLength) {
            errors.Add(new OperationError("title", $"Title must be at most {Notification.MaxTitleLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(intent.Body)) {
            errors.Add(new OperationError("body", "Body is required."));
        } else if (intent.Body.Trim().Length > Notification.MaxBodyLength) {
            errors.Add(new OperationError("body", $"Body must be at most {Notification.MaxBodyLength} characters."));
        }

        if (intent.ResourceId is { Length: > Notification.MaxResourceIdLength }) {
            errors.Add(new OperationError("resourceId", $"Resource id must be at most {Notification.MaxResourceIdLength} characters."));
        }

        if (intent.DeduplicationKey is { Length: > Notification.MaxDeduplicationKeyLength }) {
            errors.Add(new OperationError("deduplicationKey", "Deduplication key is too long."));
        }

        if (intent.Metadata is { } metadata) {
            if (metadata.Count > Notification.MaxMetadataEntries) {
                errors.Add(new OperationError("metadata", $"At most {Notification.MaxMetadataEntries} metadata entries are allowed."));
            }

            foreach (var (key, value) in metadata) {
                if (string.IsNullOrWhiteSpace(key) || key.Length > Notification.MaxMetadataKeyLength) {
                    errors.Add(new OperationError("metadata", $"Metadata keys must be 1-{Notification.MaxMetadataKeyLength} characters."));
                } else if (value.Length > Notification.MaxMetadataValueLength) {
                    errors.Add(new OperationError("metadata", $"Metadata value for '{key}' exceeds {Notification.MaxMetadataValueLength} characters."));
                }
            }
        }

        return errors;
    }
}
