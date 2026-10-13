using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Abstractions.Repositories;

public interface IDeliveryJobRepository {
    void AddRange(IEnumerable<DeliveryJob> jobs);

    /// <summary>Loads the aggregate (with its attempts) for modification.</summary>
    Task<DeliveryJob?> GetByIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically leases up to <paramref name="batchSize"/> due jobs to the caller and opens an attempt on each.
    /// Concurrent callers never receive the same job.
    /// </summary>
    Task<IReadOnlyList<ClaimedDeliveryJob>> ClaimDueJobsAsync(
        int batchSize, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>When the next job becomes due (retry time or lease expiry); <c>null</c> when nothing is waiting.</summary>
    Task<DateTimeOffset?> GetNextDueAtAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<DeliveryJobSummary>> ListHistoryAsync(
        DeliveryHistoryQuery query, CancellationToken cancellationToken = default);

    Task<DeliveryJobDetail?> GetDetailAsync(Guid jobId, CancellationToken cancellationToken = default);
}

/// <summary>A job this worker now holds, identified by the attempt it opened.</summary>
public sealed record ClaimedDeliveryJob(Guid JobId, int AttemptNumber);

public sealed record DeliveryHistoryQuery(
    int Page,
    int PageSize,
    DeliveryJobStatus? Status = null,
    string? ProviderId = null,
    Guid? NotificationId = null,
    bool SimulatedOnly = false);

public sealed record DeliveryJobSummary(
    Guid JobId,
    Guid NotificationId,
    string NotificationTitle,
    string NotificationType,
    Guid RecipientUserId,
    string RecipientUserName,
    Guid DeviceRegistrationId,
    string InstallationId,
    string ProviderId,
    DeliveryJobStatus Status,
    int AttemptCount,
    bool IsSimulated,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset CreatedAt);

public sealed record DeliveryAttemptItem(
    int AttemptNumber,
    string ProviderId,
    DeliveryAttemptStatus Status,
    DispatchOutcome? Outcome,
    bool IsSimulated,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record DeliveryJobDetail(
    DeliveryJobSummary Job,
    string? ProviderMessageId,
    bool RegistrationEnabled,
    DeviceDisabledReason? RegistrationDisabledReason,
    IReadOnlyList<DeliveryAttemptItem> Attempts);
