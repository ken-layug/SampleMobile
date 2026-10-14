using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Notifications;
using SampleMobile.Mobile.Push.Fcm;
using SampleMobile.Mobile.Services;
using SampleMobile.Mobile.Views;

namespace SampleMobile.Mobile;

/// <summary>Composition root.</summary>
public static class MauiProgram {
    public static MauiApp CreateMauiApp() {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Provider-neutral client: API, session, inbox, registration lifecycle, view models.
        builder.Services.AddMobileCore(options => {
            options.Platform = "android";
#if DEBUG
            // The host machine as seen from the Android emulator; change it on the login screen for a device.
            options.DefaultBackendUrl = "http://10.0.2.2:5010";
#endif
            // The native handler applies the app's network security config (cleartext rules, trust anchors).
            options.CreatePrimaryHttpHandler = () => new Xamarin.Android.Net.AndroidMessageHandler();
        });

        builder.Services.AddSingleton<ISecureStore, MauiSecureStore>();
        builder.Services.AddSingleton<IAppPreferences, MauiPreferences>();
        builder.Services.AddSingleton<IMainThreadDispatcher, MauiDispatcher>();
        builder.Services.AddSingleton<INetworkStatus, MauiNetworkStatus>();
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();

        // Android notifications: channel, local presentation, permission, tap intents. Not tied to a provider.
        builder.UseAndroidNotifications();

        // The push transport. Replacing Firebase means replacing this line (and its Platforms/Android/Push folder).
        builder.UseFirebasePushTransport();

        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<InboxPage>();
        builder.Services.AddTransient<NotificationDetailPage>();
        builder.Services.AddTransient<DiagnosticsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
