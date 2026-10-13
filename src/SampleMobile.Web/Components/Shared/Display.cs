using SampleMobile.Domain.Entities;

namespace SampleMobile.Web.Components.Shared;

/// <summary>Text and Bootstrap badge class for a status value.</summary>
public sealed record BadgeDisplay(string Text, string CssClass);

/// <summary>How statuses and timestamps look across the dashboard, decided in one place.</summary>
public static class Display {
    public static BadgeDisplay For(DeliveryJobStatus status) =>
        status switch {
            DeliveryJobStatus.Pending => new("Pending", "text-bg-secondary"),
            DeliveryJobStatus.Processing => new("Processing", "text-bg-info"),
            DeliveryJobStatus.RetryScheduled => new("Retry scheduled", "text-bg-warning"),
            DeliveryJobStatus.ProviderAccepted => new("Provider accepted", "text-bg-success"),
            DeliveryJobStatus.Failed => new("Failed", "text-bg-danger"),
            DeliveryJobStatus.Cancelled => new("Cancelled", "text-bg-dark"),
            _ => new(status.ToString(), "text-bg-light")
        };

    public static BadgeDisplay For(DeliveryAttemptStatus status, DispatchOutcome? outcome) =>
        (status, outcome) switch {
            (DeliveryAttemptStatus.InProgress, _) => new("In progress", "text-bg-info"),
            (DeliveryAttemptStatus.Abandoned, _) => new("Abandoned (outcome unknown)", "text-bg-warning"),
            (DeliveryAttemptStatus.Skipped, _) => new("Not sent", "text-bg-dark"),
            (_, DispatchOutcome.Accepted) => new("Accepted by provider", "text-bg-success"),
            (_, DispatchOutcome.TransientFailure) => new("Transient failure", "text-bg-warning"),
            (_, DispatchOutcome.InvalidRegistration) => new("Invalid registration", "text-bg-danger"),
            (_, DispatchOutcome.AuthenticationFailure) => new("Auth / config failure", "text-bg-danger"),
            (_, DispatchOutcome.PermanentFailure) => new("Permanent failure", "text-bg-danger"),
            _ => new(status.ToString(), "text-bg-light")
        };

    public static BadgeDisplay ForRegistration(bool isEnabled, DeviceDisabledReason? reason) =>
        isEnabled
            ? new("Enabled", "text-bg-success")
            : new($"Disabled: {Humanize(reason?.ToString() ?? "unknown")}", "text-bg-secondary");

    public static BadgeDisplay Simulated { get; } = new("Simulated", "text-bg-primary");

    public static string Time(DateTimeOffset? value) =>
        value is { } time ? time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") : "—";

    public static string ShortId(Guid id) => id.ToString("N")[^8..];

    /// <summary>"InvalidatedByProvider" → "invalidated by provider".</summary>
    public static string Humanize(string pascalCase) =>
        string.Concat(pascalCase.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? " " + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}
