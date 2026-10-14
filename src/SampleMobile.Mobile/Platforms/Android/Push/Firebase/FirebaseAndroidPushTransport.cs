using Android.Gms.Common;
using Android.Gms.Extensions;
using Firebase;
using Firebase.Messaging;
using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Push.Fcm;

/// <summary>
/// Firebase Cloud Messaging behind the provider-neutral <see cref="IPushTransport"/>. This class and
/// <see cref="SampleFirebaseMessagingService"/> are the only places that use the Firebase SDK.
/// The backend's <c>fcm</c> provider expects the FCM <em>registration token</em> as the address.
/// </summary>
public sealed class FirebaseAndroidPushTransport(TimeProvider time, ILogger<FirebaseAndroidPushTransport> logger) : IPushTransport {
    public const string Id = "fcm";

    private static readonly TimeSpan TokenTimeout = TimeSpan.FromSeconds(30);

    // FCM error codes that won't fix themselves (usually google-services.json for another package name or project).
    private static readonly string[] PermanentErrors = ["FIS_AUTH_ERROR", "AUTHENTICATION_FAILED", "MISSING_INSTANCEID_SERVICE", "INVALID_SENDER"];

    private volatile bool _initialized;

    public string ProviderId => Id;

    public string DisplayName => "Firebase Cloud Messaging";

    public bool DeliversRealPush => true;

    public event EventHandler<PushAddressChangedEventArgs>? AddressChanged;

    public event EventHandler<PushMessageReceivedEventArgs>? MessageReceived;

    // FCM reports taps on its tray notifications as launcher intents, handled by the app's intent handler.
    public event EventHandler<PushNotificationOpenedEventArgs>? NotificationOpened { add { } remove { } }

    public Task<PushTransportStatus> InitializeAsync(CancellationToken cancellationToken) {
        var context = global::Android.App.Application.Context;

        var availability = GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(context);
        if (availability != ConnectionResult.Success) {
            var reason = $"Google Play services unavailable: {GoogleApiAvailability.Instance.GetErrorString(availability)}";
            return Task.FromResult(new PushTransportStatus(PushTransportState.Unavailable, reason));
        }

        // Returns the default app (already created by FirebaseInitProvider) or null when the build has no
        // google-services.json resources. A missing configuration is a state, not a crash.
        if (FirebaseApp.InitializeApp(context) is null) {
            return Task.FromResult(new PushTransportStatus(PushTransportState.NotConfigured, "google-services.json is not part of this build"));
        }

        _initialized = true;
        return Task.FromResult(PushTransportStatus.Available);
    }

    public Task ActivateAsync(CancellationToken cancellationToken) {
        if (_initialized) {
            // The manifest disables auto-init, so no token exists before a user signs in.
            FirebaseMessaging.Instance.AutoInitEnabled = true;
        }

        return Task.CompletedTask;
    }

    public async Task<PushAddressResult> GetRegistrationAddressAsync(CancellationToken cancellationToken) {
        if (!_initialized) {
            return PushAddressResult.Permanent("Firebase is not initialized");
        }

        try {
#pragma warning disable CS0618
            // firebase-messaging 25 deprecates registration tokens in favour of Firebase Installation IDs. The backend's
            // "fcm" provider is defined as "address = FCM registration token" (see FcmMessageFactory); FIDs would be a
            // different address type and get their own provider id, so this transport keeps using the token API.
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>().WaitAsync(TokenTimeout, cancellationToken);
#pragma warning restore CS0618
            var value = token?.ToString();
            return string.IsNullOrEmpty(value) ? PushAddressResult.Transient("FCM returned no token") : PushAddressResult.Success(value);
        } catch (TimeoutException) {
            return PushAddressResult.Transient("Timed out waiting for an FCM token");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            var message = ex.Message;
            logger.LogWarning("Getting the FCM token failed: {Error}", message);
            return PermanentErrors.Any(code => message.Contains(code, StringComparison.Ordinal))
                ? PushAddressResult.Permanent(message)
                : PushAddressResult.Transient(message);
        }
    }

    public async Task<bool> ResetRegistrationAsync(CancellationToken cancellationToken) {
        if (!_initialized) {
            var status = await InitializeAsync(cancellationToken);
            if (status.State == PushTransportState.NotConfigured) {
                return true; // Nothing was ever issued in this build.
            }

            if (status.State != PushTransportState.Available) {
                return false;
            }
        }

        try {
            // Stop automatic re-issuing first, then delete: the old token becomes invalid at FCM, so a registration
            // the backend still holds for the previous user can no longer deliver to this device.
            FirebaseMessaging.Instance.AutoInitEnabled = false;
#pragma warning disable CS0618 // Registration-token API on purpose; see GetRegistrationAddressAsync.
            await FirebaseMessaging.Instance.DeleteToken().AsAsync<Java.Lang.Object>().WaitAsync(TokenTimeout, cancellationToken);
#pragma warning restore CS0618
            return true;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning("Deleting the FCM token failed (will retry): {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>Called by <see cref="SampleFirebaseMessagingService.OnNewToken"/>.</summary>
    internal void OnNewToken(string token) => AddressChanged?.Invoke(this, new PushAddressChangedEventArgs(Id, token));

    /// <summary>
    /// Called by <see cref="SampleFirebaseMessagingService.OnMessageReceived"/>. FCM only calls it when it did not
    /// display the message itself (app in the foreground, or a data-only message), so nothing was shown yet.
    /// Title/body from the FCM notification block are deliberately dropped: the app renders API data only.
    /// </summary>
    internal void OnMessageReceived(RemoteMessage message) {
        var data = message.Data?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        var push = new PushMessage(Id, data, HasDisplayedNotification: false, time.GetUtcNow(), message.MessageId);
        MessageReceived?.Invoke(this, new PushMessageReceivedEventArgs(push));
    }
}
