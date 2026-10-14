using Android.App;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Notifications;

// FCM shows notification messages itself while the app is in the background; these make it use the app's
// channel and status-bar icon. No token is created before a user signs in (auto-init is enabled at sign-in).
[assembly: MetaData("com.google.firebase.messaging.default_notification_channel_id", Value = NotificationChannels.DefaultId)]
[assembly: MetaData("com.google.firebase.messaging.default_notification_icon", Resource = "@drawable/ic_stat_notification")]
[assembly: MetaData("firebase_messaging_auto_init_enabled", Value = "false")]
[assembly: MetaData("firebase_analytics_collection_enabled", Value = "false")]

namespace SampleMobile.Mobile.Push.Fcm;

/// <summary>Registers FCM as this app's real push transport.</summary>
public static class FirebasePushTransportExtensions {
    public static MauiAppBuilder UseFirebasePushTransport(this MauiAppBuilder builder) {
        // One instance serves both the push service (by concrete type) and the core (as IPushTransport).
        builder.Services.AddSingleton<FirebaseAndroidPushTransport>();
        builder.Services.AddSingleton<IPushTransport>(sp => sp.GetRequiredService<FirebaseAndroidPushTransport>());
        return builder;
    }
}
