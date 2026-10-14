using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.BackgroundServices;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Infrastructure.Persistence.Interceptors;

/// <summary>
/// After a save that created work (new or re-queued delivery jobs, new outbox rows), sends
/// <c>NOTIFY</c> so every process listening wakes immediately instead of polling. Inside an explicit
/// transaction PostgreSQL holds the notification until commit, so nobody wakes for uncommitted rows.
/// The timed wake-up in each processor remains the safety net if a notification is ever missed.
/// </summary>
/// <remarks>Scoped: it carries the channels detected in SavingChanges over to SavedChanges for one context.</remarks>
public sealed class WorkAvailableInterceptor(WorkSignal signal) : SaveChangesInterceptor {
    private readonly HashSet<string> _pendingChannels = new(StringComparer.Ordinal);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) {
        if (eventData.Context is { } context && _pendingChannels.Count > 0) {
            foreach (var channel in _pendingChannels) {
                await context.Database.ExecuteSqlAsync($"SELECT pg_notify({channel}, '')", cancellationToken);
                signal.Notify(channel);
            }

            _pendingChannels.Clear();
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) {
        if (eventData.Context is { } context && _pendingChannels.Count > 0) {
            foreach (var channel in _pendingChannels) {
                context.Database.ExecuteSql($"SELECT pg_notify({channel}, '')");
                signal.Notify(channel);
            }

            _pendingChannels.Clear();
        }

        return base.SavedChanges(eventData, result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) {
        _pendingChannels.Clear();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) {
        _pendingChannels.Clear();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Collect(DbContext? context) {
        if (context is null) {
            return;
        }

        var deliveryWork = context.ChangeTracker.Entries<DeliveryJob>().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified
            && entry.Entity.Status is DeliveryJobStatus.Pending or DeliveryJobStatus.RetryScheduled);
        if (deliveryWork) {
            _pendingChannels.Add(WorkChannels.Delivery);
        }

        if (context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added)) {
            _pendingChannels.Add(WorkChannels.Outbox);
        }
    }
}
