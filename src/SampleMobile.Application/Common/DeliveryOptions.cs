using SampleMobile.Domain.Services;

namespace SampleMobile.Application.Common;

/// <summary>Tuning for push delivery: retry budget, worker leases and batching. Bound from the <c>Delivery</c> section.</summary>
public sealed class DeliveryOptions {
    public const string SectionName = "Delivery";

    /// <summary>Attempts per run (creation or requeue) before a transient failure becomes final.</summary>
    public int MaxAttempts { get; set; } = 5;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a claimed job stays reserved for one worker before another may take it over.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Upper bound for a single provider call; must stay well below <see cref="LeaseDuration"/>.</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; set; } = 20;
    public int MaxParallelism { get; set; } = 4;

    /// <summary>Longest the worker sleeps without a wake-up signal, as a safety net for missed notifications.</summary>
    public TimeSpan MaxIdleWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Turns the background worker off (tests drive dispatch directly).</summary>
    public bool WorkerEnabled { get; set; } = true;

    public DeliveryRetryPolicy CreateRetryPolicy() => new(MaxAttempts, RetryBaseDelay, RetryMaxDelay);
}
