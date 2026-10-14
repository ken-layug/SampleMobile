using Android.App;
using Firebase.Messaging;

namespace SampleMobile.Mobile.Push.Fcm;

/// <summary>
/// The FCM entry point Android starts. It may run in a process that has no UI (Application.OnCreate has run, so
/// the service provider exists) and only forwards to <see cref="FirebaseAndroidPushTransport"/>.
/// Notification messages for a backgrounded app never arrive here: Android shows them in the tray and the tap
/// arrives as a launch intent.
/// </summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class SampleFirebaseMessagingService : FirebaseMessagingService {
#pragma warning disable CS0672 // Registration-token callback on purpose; see FirebaseAndroidPushTransport.GetRegistrationAddressAsync.
    public override void OnNewToken(string token) => Transport?.OnNewToken(token);
#pragma warning restore CS0672

    public override void OnMessageReceived(RemoteMessage message) => Transport?.OnMessageReceived(message);

    private static FirebaseAndroidPushTransport? Transport =>
        IPlatformApplication.Current?.Services.GetService<FirebaseAndroidPushTransport>();
}
