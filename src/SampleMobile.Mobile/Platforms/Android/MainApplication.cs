using Android.App;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Notifications;

namespace SampleMobile.Mobile;

#if DEBUG
// Debug builds may talk plain HTTP to the emulator host / loopback and trust user-installed dev CAs.
[Application(NetworkSecurityConfig = "@xml/network_security_config_debug")]
#else
[Application(NetworkSecurityConfig = "@xml/network_security_config")]
#endif
public class MainApplication : MauiApplication {
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership) {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Runs before any activity or service, including a push service started without UI.
    public override void OnCreate() {
        base.OnCreate();

        NotificationChannels.EnsureCreated(this);
        var services = IPlatformApplication.Current!.Services;

        // Subscribe to push transport events before any can be raised.
        services.GetRequiredService<PushEventProcessor>().Start();

        var bootstrapper = services.GetRequiredService<SessionBootstrapper>();
        var logger = services.GetRequiredService<ILogger<MainApplication>>();
        services.GetRequiredService<INetworkStatus>().ConnectivityRestored += async (_, _) => {
            try {
                await bootstrapper.ResumeAsync(RegistrationSyncReason.Connectivity);
            } catch (Exception ex) {
                logger.LogWarning(ex, "Work after reconnecting failed");
            }
        };
    }
}
