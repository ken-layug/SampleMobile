using Android.App;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Notifications;

/// <summary>
/// Posts the app's own notifications (for pushes handed to the running app, which the OS doesn't display).
/// Entries use the notification id as tag and 0 as id, exactly like the tray entries the push provider posts
/// (the backend sets the Android tag to the notification id), so the same notification never stacks twice.
/// </summary>
public sealed class AndroidLocalNotificationPresenter(ILogger<AndroidLocalNotificationPresenter> logger) : ILocalNotificationPresenter {
    private const int NotificationIdWithinTag = 0;

    public void Show(InboxNotification notification) =>
        Post(notification.Id, notification.Title, notification.Body, notification.Type, notification.ResourceId);

    public void ShowGeneric(Guid notificationId) =>
        Post(notificationId, "New notification", "Tap to open it.", type: null, resourceId: null);

    public void CancelAll() => NotificationManagerCompat.From(Platform.AppContext)?.CancelAll();

    private void Post(Guid notificationId, string title, string body, string? type, string? resourceId) {
        var context = Platform.AppContext;
        var manager = NotificationManagerCompat.From(context);
        if (manager is null || !manager.AreNotificationsEnabled()) {
            logger.LogInformation("Notifications are disabled for the app; {NotificationId} is only in the inbox", notificationId);
            return;
        }

        var intent = NotificationTapIntents.Create(context, notificationId, type, resourceId);
        var tap = PendingIntent.GetActivity(context, RequestCode(notificationId), intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new NotificationCompat.Builder(context, NotificationChannels.DefaultId)
            .SetSmallIcon(Resource.Drawable.ic_stat_notification)!
            .SetContentTitle(title)!
            .SetContentText(body)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))!
            .SetPriority(NotificationCompat.PriorityHigh)!
            .SetAutoCancel(true)!
            .SetContentIntent(tap)!
            .Build()!;

        try {
            manager.Notify(notificationId.ToString("D"), NotificationIdWithinTag, notification);
        } catch (Java.Lang.SecurityException ex) {
            // POST_NOTIFICATIONS was revoked between the check and the call.
            logger.LogInformation(ex, "Not allowed to post a notification");
        }
    }

    // Deterministic per notification (string.GetHashCode is randomized per process).
    private static int RequestCode(Guid notificationId) => BitConverter.ToInt32(notificationId.ToByteArray(), 0);
}
