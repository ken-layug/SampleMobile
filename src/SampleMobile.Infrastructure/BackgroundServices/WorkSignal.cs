using System.Collections.Concurrent;

namespace SampleMobile.Infrastructure.BackgroundServices;

/// <summary>PostgreSQL LISTEN/NOTIFY channel names used to wake the background processors.</summary>
public static class WorkChannels {
    public const string Delivery = "sample_mobile_delivery";
    public const string Outbox = "sample_mobile_outbox";

    public static IReadOnlyList<string> All { get; } = [Delivery, Outbox];
}

/// <summary>
/// In-process wake-up for the background processors: an auto-reset signal per channel. Producers set
/// it after committing work; <see cref="PostgresWorkListener"/> sets it when another process commits.
/// Several notifications before a wait collapse into one wake-up, which is all a "go look" signal needs.
/// </summary>
public sealed class WorkSignal {
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _signals = new(StringComparer.Ordinal);

    public void Notify(string channel) {
        var signal = Get(channel);
        if (signal.CurrentCount > 0) {
            return;
        }

        try {
            signal.Release();
        } catch (SemaphoreFullException) {
            // Another thread set it between the check and the release; one pending wake-up is enough.
        }
    }

    public void NotifyAll() {
        foreach (var channel in WorkChannels.All) {
            Notify(channel);
        }
    }

    /// <summary>Waits until the channel is notified or the timeout elapses. Returns <c>true</c> when notified.</summary>
    public Task<bool> WaitAsync(string channel, TimeSpan timeout, CancellationToken cancellationToken) =>
        Get(channel).WaitAsync(timeout <= TimeSpan.Zero ? TimeSpan.Zero : timeout, cancellationToken);

    private SemaphoreSlim Get(string channel) => _signals.GetOrAdd(channel, static _ => new SemaphoreSlim(0, 1));
}
