namespace SampleMobile.Infrastructure.BackgroundServices;

/// <summary>How long a processor sleeps when it found nothing to claim.</summary>
internal static class WaitPolicy {
    /// <summary>
    /// A row can be due yet unclaimable for a moment (another transaction holds its lock), so never spin:
    /// wait at least this long even when the next due time has already passed.
    /// </summary>
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(250);

    /// <summary>Sleep until the next due time, bounded by <paramref name="maxIdleWait"/> as a safety net.</summary>
    public static TimeSpan Until(DateTimeOffset? nextDue, DateTimeOffset now, TimeSpan maxIdleWait) {
        if (nextDue is not { } due) {
            return maxIdleWait;
        }

        var untilDue = due - now;
        if (untilDue < MinimumWait) {
            return MinimumWait;
        }

        return untilDue < maxIdleWait ? untilDue : maxIdleWait;
    }
}
