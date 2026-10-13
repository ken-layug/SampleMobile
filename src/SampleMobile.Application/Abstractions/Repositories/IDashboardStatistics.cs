namespace SampleMobile.Application.Abstractions.Repositories;

/// <summary>Cross-aggregate counters for the dashboard overview. Read-only.</summary>
public interface IDashboardStatistics {
    Task<DashboardStats> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record DashboardStats(
    int TotalNotifications,
    int PendingDeliveries,
    int ProviderAcceptedDeliveries,
    int FailedDeliveries,
    int CancelledDeliveries,
    int SimulatedDispatches,
    int ActiveDeviceRegistrations,
    int OutboxPending,
    int OutboxProcessed,
    int OutboxFailed);
