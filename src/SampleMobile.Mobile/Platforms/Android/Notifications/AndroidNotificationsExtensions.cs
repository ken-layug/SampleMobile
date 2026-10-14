using Android.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Notifications;

/// <summary>Android notification plumbing that is the same whichever push provider is used.</summary>
public static class AndroidNotificationsExtensions {
    public static MauiAppBuilder UseAndroidNotifications(this MauiAppBuilder builder) {
        builder.Services.AddSingleton<ILocalNotificationPresenter, AndroidLocalNotificationPresenter>();
        builder.Services.AddSingleton<AndroidNotificationPermissionService>();
        builder.Services.AddSingleton<INotificationPermissionService>(sp => sp.GetRequiredService<AndroidNotificationPermissionService>());

        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android => android
            // Cold start from a tray tap (process not running, or activity destroyed). A non-null bundle means the
            // activity is being recreated (rotation, process restore) and the tap was already handled.
            .OnCreate((activity, savedInstanceState) => {
                if (activity is MainActivity && savedInstanceState is null) {
                    HandleTap(activity.Intent);
                }
            })
            // Tap while the app is running (SingleTop activity).
            .OnNewIntent((activity, intent) => {
                if (activity is MainActivity) {
                    HandleTap(intent);
                }
            })
            .OnResume(activity => {
                if (activity is MainActivity) {
                    Services?.GetService<AndroidNotificationPermissionService>()?.OnAppResumed();
                }
            })));

        return builder;
    }

    private static IServiceProvider? Services => IPlatformApplication.Current?.Services;

    private static async void HandleTap(Intent? intent) {
        if (NotificationTapIntents.Take(intent) is not { } data || Services is not { } services) {
            return;
        }

        try {
            // Navigation waits until a session is restored (or the user signs in) and the shell is shown.
            await services.GetRequiredService<PushEventProcessor>().HandleOpenedAsync(data, "notification tap");
        } catch (Exception ex) {
            services.GetRequiredService<ILogger<PushEventProcessor>>().LogWarning(ex, "Handling a notification tap failed");
        }
    }
}
