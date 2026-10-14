using Android.Content;
using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Notifications;

/// <summary>
/// The launch intent of a notification tap. A tray notification shown by the push provider while the app was in
/// the background opens the launcher activity with the push data keys as extras; the app's own notifications
/// build the same intent. Reading it therefore doesn't depend on which provider delivered the push.
/// </summary>
public static class NotificationTapIntents {
    private const string Scheme = "samplemobile";

    private static readonly string[] Keys = [PushDataKeys.NotificationId, PushDataKeys.Type, PushDataKeys.ResourceId];

    /// <summary>An intent for a local notification. The data URI keeps PendingIntents for different ids distinct.</summary>
    public static Intent Create(Context context, Guid notificationId, string? type, string? resourceId) {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetData(global::Android.Net.Uri.Parse($"{Scheme}://notifications/{notificationId:D}"));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        intent.PutExtra(PushDataKeys.NotificationId, notificationId.ToString("D"));
        if (type is not null) {
            intent.PutExtra(PushDataKeys.Type, type);
        }

        if (resourceId is not null) {
            intent.PutExtra(PushDataKeys.ResourceId, resourceId);
        }

        return intent;
    }

    /// <summary>
    /// Returns the push data of a tap, or null when the intent isn't one (a normal launch, or a relaunch from
    /// Recents that replays the original intent). The data is removed so it can't be handled twice.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Take(Intent? intent) {
        if (intent?.Extras is not { } extras || intent.Flags.HasFlag(ActivityFlags.LaunchedFromHistory)) {
            return null;
        }

        var data = new Dictionary<string, string>();
        foreach (var key in Keys) {
            if (extras.GetString(key) is { } value) {
                data[key] = value;
            }
        }

        if (data.Count == 0) {
            return null;
        }

        foreach (var key in Keys) {
            intent.RemoveExtra(key);
        }

        return data;
    }
}
