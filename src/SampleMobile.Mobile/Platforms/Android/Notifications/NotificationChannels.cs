using Android.App;
using Android.Content;

namespace SampleMobile.Mobile.Notifications;

/// <summary>
/// The app's notification channel. Both the app's own notifications and tray notifications a push provider
/// shows while the app is in the background use it (the provider's default-channel meta-data points here;
/// the backend can also name it via <c>Firebase:AndroidChannelId</c>).
/// </summary>
public static class NotificationChannels {
    public const string DefaultId = "sample_mobile_default";

    /// <summary>Creates the channel (idempotent). Runs at process start, before any notification can arrive.</summary>
    public static void EnsureCreated(Context context) {
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        var channel = new NotificationChannel(DefaultId, "Notifications", NotificationImportance.High) {
            Description = "Alerts and messages from SampleMobile"
        };
        manager?.CreateNotificationChannel(channel);
    }
}
