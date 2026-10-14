using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Core.ViewModels;

/// <summary>Formatting shared by the view models.</summary>
internal static class DisplayText {
    public static string Time(DateTimeOffset? value, TimeProvider time) =>
        value is { } at ? TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).ToString("g") : "never";

    public static string Permission(NotificationPermissionState state) => state switch {
        NotificationPermissionState.Granted => "Allowed",
        NotificationPermissionState.Denied => "Blocked (notifications won't be shown; the inbox still works)",
        NotificationPermissionState.NotRequested => "Not requested yet",
        _ => "Unknown"
    };

    public static string Registration(RegistrationStatus status, RegistrationResult? last, TimeProvider time) {
        var state = status switch {
            RegistrationStatus.Registered => "Registered",
            RegistrationStatus.Syncing => "Syncing…",
            RegistrationStatus.PendingRetry => "Waiting to retry (backend unreachable)",
            RegistrationStatus.Failed => "Failed",
            _ => "Not registered"
        };

        return last is null ? state : $"{state} · last: {last.Summary} ({Time(last.At, time)})";
    }

    public static string Preview(string body) {
        var singleLine = body.ReplaceLineEndings(" ");
        return singleLine.Length <= 120 ? singleLine : singleLine[..117] + "…";
    }
}
