namespace SampleMobile.Domain.Services;

/// <summary>
/// Bounded exponential backoff: the delay after attempt <c>n</c> is <c>BaseDelay × 2^(n−1)</c>,
/// capped at <see cref="MaxDelay"/>, and no retry is offered once <see cref="MaxAttempts"/> is reached.
/// Pure and deterministic so retry timing is easy to test; add jitter here if many jobs ever fail together.
/// </summary>
public sealed record DeliveryRetryPolicy {
    public DeliveryRetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, baseDelay);

        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay;
        MaxDelay = maxDelay;
    }

    public int MaxAttempts { get; }
    public TimeSpan BaseDelay { get; }
    public TimeSpan MaxDelay { get; }

    /// <summary>
    /// Returns how long to wait before the next attempt, or <c>null</c> when the retry budget is spent.
    /// </summary>
    /// <param name="attemptsMade">Attempts already made in the current run (1 after the first failure).</param>
    public TimeSpan? GetDelayAfter(int attemptsMade) {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptsMade, 1);
        if (attemptsMade >= MaxAttempts) {
            return null;
        }

        // Computed in floating point so large attempt numbers saturate at MaxDelay instead of overflowing.
        var multiplier = Math.Pow(2, attemptsMade - 1);
        var delayTicks = Math.Min(BaseDelay.Ticks * multiplier, MaxDelay.Ticks);
        return TimeSpan.FromTicks((long)delayTicks);
    }
}
