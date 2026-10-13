using Microsoft.EntityFrameworkCore;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Persistence.Repositories;

internal sealed class DeliveryJobRepository(AppDbContext db) : IDeliveryJobRepository {
    public void AddRange(IEnumerable<DeliveryJob> jobs) => db.DeliveryJobs.AddRange(jobs);

    public Task<DeliveryJob?> GetByIdAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        db.DeliveryJobs
            .Include(job => job.Attempts)
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);

    /// <summary>
    /// <c>FOR UPDATE SKIP LOCKED</c> makes concurrent workers (threads or whole processes) skip rows another
    /// claim transaction is holding, so no two workers lease the same job. The transaction only covers the
    /// claim itself; the provider call happens later, outside any transaction, protected by the lease.
    /// </summary>
    public async Task<IReadOnlyList<ClaimedDeliveryJob>> ClaimDueJobsAsync(
        int batchSize, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken = default) {
        var pending = nameof(DeliveryJobStatus.Pending);
        var retryScheduled = nameof(DeliveryJobStatus.RetryScheduled);
        var processing = nameof(DeliveryJobStatus.Processing);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // xmin is a system column, so SELECT * doesn't return it; EF needs it for the concurrency token.
        var jobs = await db.DeliveryJobs
            .FromSql($"""
                SELECT j.*, j.xmin
                FROM notification.delivery_jobs AS j
                WHERE (j.status IN ({pending}, {retryScheduled}) AND j.next_attempt_at <= {now})
                   OR (j.status = {processing} AND j.locked_until <= {now})
                ORDER BY j.next_attempt_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0) {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        // Attempts are needed so an attempt left in flight by a crashed worker can be marked abandoned.
        var jobIds = jobs.Select(job => job.Id).ToList();
        await db.DeliveryAttempts.Where(attempt => jobIds.Contains(attempt.DeliveryJobId)).LoadAsync(cancellationToken);

        var claims = jobs
            .Select(job => new ClaimedDeliveryJob(job.Id, job.Claim(now, lease).AttemptNumber))
            .ToList();

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    public async Task<DateTimeOffset?> GetNextDueAtAsync(CancellationToken cancellationToken = default) {
        var nextAttempt = await db.DeliveryJobs
            .Where(job => job.Status == DeliveryJobStatus.Pending || job.Status == DeliveryJobStatus.RetryScheduled)
            .MinAsync(job => (DateTimeOffset?)job.NextAttemptAt, cancellationToken);
        var nextLeaseExpiry = await db.DeliveryJobs
            .Where(job => job.Status == DeliveryJobStatus.Processing)
            .MinAsync(job => job.LockedUntil, cancellationToken);

        return (nextAttempt, nextLeaseExpiry) switch {
            ({ } attempt, { } lease) => attempt < lease ? attempt : lease,
            ({ } attempt, null) => attempt,
            (null, { } lease) => lease,
            _ => null
        };
    }

    public async Task<PagedResult<DeliveryJobSummary>> ListHistoryAsync(
        DeliveryHistoryQuery query, CancellationToken cancellationToken = default) {
        var jobs = db.DeliveryJobs.AsNoTracking().AsQueryable();
        if (query.Status is { } status) {
            jobs = jobs.Where(job => job.Status == status);
        }

        if (query.ProviderId is { } providerId) {
            jobs = jobs.Where(job => job.ProviderId == providerId);
        }

        if (query.NotificationId is { } notificationId) {
            jobs = jobs.Where(job => job.NotificationId == notificationId);
        }

        if (query.SimulatedOnly) {
            jobs = jobs.Where(job => db.DeliveryAttempts.Any(attempt => attempt.DeliveryJobId == job.Id && attempt.IsSimulated)
                || db.DeviceRegistrations.Any(registration => registration.Id == job.DeviceRegistrationId
                    && registration.Platform == DevicePlatform.Virtual));
        }

        var totalCount = await jobs.CountAsync(cancellationToken);
        var rows = await Project(jobs)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.JobId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DeliveryJobSummary>(rows.Select(ToSummary).ToList(), query.Page, query.PageSize, totalCount);
    }

    public async Task<DeliveryJobDetail?> GetDetailAsync(Guid jobId, CancellationToken cancellationToken = default) {
        var row = await Project(db.DeliveryJobs.AsNoTracking().Where(job => job.Id == jobId)).FirstOrDefaultAsync(cancellationToken);
        if (row is null) {
            return null;
        }

        var attempts = await db.DeliveryAttempts.AsNoTracking()
            .Where(attempt => attempt.DeliveryJobId == jobId)
            .OrderBy(attempt => attempt.AttemptNumber)
            .Select(attempt => new DeliveryAttemptItem(
                attempt.AttemptNumber,
                attempt.ProviderId,
                attempt.Status,
                attempt.Outcome,
                attempt.IsSimulated,
                attempt.ProviderMessageId,
                attempt.ErrorCode,
                attempt.ErrorMessage,
                attempt.StartedAt,
                attempt.CompletedAt))
            .ToListAsync(cancellationToken);

        return new DeliveryJobDetail(ToSummary(row), row.ProviderMessageId, row.RegistrationEnabled, row.RegistrationDisabledReason, attempts);
    }

    // Member-initializer projection (not a constructor) so EF can still order and page over its properties.
    private IQueryable<JobRow> Project(IQueryable<DeliveryJob> jobs) =>
        from job in jobs
        join notification in db.Notifications.AsNoTracking() on job.NotificationId equals notification.Id
        join registration in db.DeviceRegistrations.AsNoTracking() on job.DeviceRegistrationId equals registration.Id
        join user in db.Users.AsNoTracking() on job.RecipientUserId equals user.Id
        select new JobRow {
            JobId = job.Id,
            NotificationId = job.NotificationId,
            NotificationTitle = notification.Title,
            NotificationType = notification.Type,
            RecipientUserId = job.RecipientUserId,
            RecipientUserName = user.UserName,
            DeviceRegistrationId = job.DeviceRegistrationId,
            InstallationId = registration.InstallationId,
            ProviderId = job.ProviderId,
            Status = job.Status,
            AttemptCount = job.AttemptCount,
            IsSimulated = job.IsSimulated || registration.Platform == DevicePlatform.Virtual,
            LastErrorCode = job.LastErrorCode,
            LastErrorMessage = job.LastErrorMessage,
            LastAttemptAt = job.LastAttemptAt,
            NextAttemptAt = job.NextAttemptAt,
            CreatedAt = job.CreatedAt,
            ProviderMessageId = job.ProviderMessageId,
            RegistrationEnabled = registration.IsEnabled,
            RegistrationDisabledReason = registration.DisabledReason
        };

    private static DeliveryJobSummary ToSummary(JobRow row) =>
        new(
            row.JobId,
            row.NotificationId,
            row.NotificationTitle,
            row.NotificationType.Value,
            row.RecipientUserId,
            row.RecipientUserName ?? string.Empty,
            row.DeviceRegistrationId,
            row.InstallationId,
            row.ProviderId,
            row.Status,
            row.AttemptCount,
            row.IsSimulated,
            row.LastErrorCode,
            row.LastErrorMessage,
            row.LastAttemptAt,
            row.NextAttemptAt,
            row.CreatedAt);

    private sealed class JobRow {
        public Guid JobId { get; init; }
        public Guid NotificationId { get; init; }
        public string NotificationTitle { get; init; } = string.Empty;
        public NotificationType NotificationType { get; init; } = null!;
        public Guid RecipientUserId { get; init; }
        public string? RecipientUserName { get; init; }
        public Guid DeviceRegistrationId { get; init; }
        public string InstallationId { get; init; } = string.Empty;
        public string ProviderId { get; init; } = string.Empty;
        public DeliveryJobStatus Status { get; init; }
        public int AttemptCount { get; init; }
        public bool IsSimulated { get; init; }
        public string? LastErrorCode { get; init; }
        public string? LastErrorMessage { get; init; }
        public DateTimeOffset? LastAttemptAt { get; init; }
        public DateTimeOffset NextAttemptAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string? ProviderMessageId { get; init; }
        public bool RegistrationEnabled { get; init; }
        public DeviceDisabledReason? RegistrationDisabledReason { get; init; }
    }
}
