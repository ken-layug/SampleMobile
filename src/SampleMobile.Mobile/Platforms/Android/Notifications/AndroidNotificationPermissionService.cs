using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Notifications;

/// <summary>
/// Android 13+ <c>POST_NOTIFICATIONS</c> plus the app's notification switch in system settings. Android can't
/// tell "never asked" from "denied", so the app remembers whether it asked.
/// </summary>
public sealed class AndroidNotificationPermissionService(IAppPreferences preferences, ILogger<AndroidNotificationPermissionService> logger)
    : INotificationPermissionService {
    private const string RequestedKey = "notifications.permissionRequested";

    public event EventHandler? Changed;

    public async Task<NotificationPermissionState> GetStatusAsync() {
        var enabled = NotificationManagerCompat.From(Platform.AppContext)?.AreNotificationsEnabled() ?? false;
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) {
            // No runtime permission before Android 13; only the settings switch.
            return enabled ? NotificationPermissionState.Granted : NotificationPermissionState.Denied;
        }

        var status = await MainThread.InvokeOnMainThreadAsync(Permissions.CheckStatusAsync<Permissions.PostNotifications>);
        if (status == PermissionStatus.Granted) {
            return enabled ? NotificationPermissionState.Granted : NotificationPermissionState.Denied;
        }

        return preferences.Get(RequestedKey) is null ? NotificationPermissionState.NotRequested : NotificationPermissionState.Denied;
    }

    public async Task<NotificationPermissionState> RequestIfNeededAsync() {
        var current = await GetStatusAsync();
        if (current != NotificationPermissionState.NotRequested) {
            return current;
        }

        try {
            await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
            preferences.Set(RequestedKey, "true");
        } catch (Exception ex) {
            // No activity yet (for example a background start); ask again next time a page is shown.
            logger.LogInformation(ex, "Could not request the notification permission now");
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return await GetStatusAsync();
    }

    public void OpenSettings() => AppInfo.Current.ShowSettingsUI();

    /// <summary>The user may have changed the setting while the app was in the background.</summary>
    public void OnAppResumed() => Changed?.Invoke(this, EventArgs.Empty);
}
