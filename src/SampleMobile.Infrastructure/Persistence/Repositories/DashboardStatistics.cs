using Microsoft.EntityFrameworkCore;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Infrastructure.Persistence.Repositories;

internal sealed class DashboardStatistics(AppDbContext db) : IDashboardStatistics {
    public async Task<DashboardStats> GetAsync(CancellationToken cancellationToken = default) {
        var jobsByStatus = await db.DeliveryJobs
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);
        var outboxByStatus = await db.OutboxMessages
            .GroupBy(message => message.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);

        int Jobs(DeliveryJobStatus status) => jobsByStatus.GetValueOrDefault(status);

        return new DashboardStats(
            TotalNotifications: await db.Notifications.CountAsync(cancellationToken),
            PendingDeliveries: Jobs(DeliveryJobStatus.Pending) + Jobs(DeliveryJobStatus.Processing) + Jobs(DeliveryJobStatus.RetryScheduled),
            ProviderAcceptedDeliveries: Jobs(DeliveryJobStatus.ProviderAccepted),
            FailedDeliveries: Jobs(DeliveryJobStatus.Failed),
            CancelledDeliveries: Jobs(DeliveryJobStatus.Cancelled),
            SimulatedDispatches: await db.DeliveryAttempts.CountAsync(attempt => attempt.IsSimulated, cancellationToken),
            ActiveDeviceRegistrations: await db.DeviceRegistrations.CountAsync(registration => registration.IsEnabled, cancellationToken),
            OutboxPending: outboxByStatus.GetValueOrDefault(OutboxMessageStatus.Pending),
            OutboxProcessed: outboxByStatus.GetValueOrDefault(OutboxMessageStatus.Processed),
            OutboxFailed: outboxByStatus.GetValueOrDefault(OutboxMessageStatus.Failed));
    }
}
