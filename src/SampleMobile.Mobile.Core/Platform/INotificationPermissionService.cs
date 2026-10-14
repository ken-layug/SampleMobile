namespace SampleMobile.Mobile.Core.Platform;

/// <summary>Whether the app may post notifications (Android 13+ <c>POST_NOTIFICATIONS</c>, or the app's notification switch).</summary>
public enum NotificationPermissionState {
    Unknown,
    /// <summary>The user hasn't been asked yet.</summary>
    NotRequested,
    Granted,
    /// <summary>Denied, or notifications are switched off for the app in system settings.</summary>
    Denied
}

/// <summary>Checks and requests the permission to display notifications.</summary>
public interface INotificationPermissionService {
    Task<NotificationPermissionState> GetStatusAsync();

    /// <summary>Shows the system prompt only if the user hasn't been asked before; otherwise returns the current state.</summary>
    Task<NotificationPermissionState> RequestIfNeededAsync();

    /// <summary>Opens the app's system settings so a denied permission can be granted manually.</summary>
    void OpenSettings();

    event EventHandler? Changed;
}
