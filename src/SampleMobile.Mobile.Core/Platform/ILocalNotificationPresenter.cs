using SampleMobile.Mobile.Core.Notifications;

namespace SampleMobile.Mobile.Core.Platform;

/// <summary>
/// Shows notifications the app builds itself (a push handed to the running app is not displayed by the OS).
/// Entries are keyed by notification id, so showing the same id again replaces the earlier entry.
/// </summary>
public interface ILocalNotificationPresenter {
    /// <summary>Shows a notification with the authoritative text fetched from the API.</summary>
    void Show(InboxNotification notification);

    /// <summary>Shows a content-free "new notification" entry when the API couldn't be reached.</summary>
    void ShowGeneric(Guid notificationId);

    /// <summary>Removes every notification of this app from the tray (on sign-out).</summary>
    void CancelAll();
}
