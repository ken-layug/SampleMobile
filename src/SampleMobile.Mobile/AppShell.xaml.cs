using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Navigation;
using SampleMobile.Mobile.Views;

namespace SampleMobile.Mobile;

/// <summary>
/// The signed-in UI. A new instance is created for every sign-in, so no page or view model state of a previous
/// user survives. Once it is on screen, a notification tap that was waiting (cold start, login) is opened.
/// </summary>
public partial class AppShell : Shell {
    private readonly NavigationCoordinator _coordinator;
    private readonly ILogger<AppShell> _logger;
    private bool _announced;

    static AppShell() {
        Routing.RegisterRoute(Routes.Notification, typeof(NotificationDetailPage));
    }

    public AppShell(NavigationCoordinator coordinator, ILogger<AppShell> logger) {
        InitializeComponent();
        _coordinator = coordinator;
        _logger = logger;
#if !DEBUG
        // Diagnostics are for development only.
        MainTabBar.Items.Remove(DiagnosticsTab);
#endif
        Unloaded += (_, _) => _coordinator.OnShellClosed();
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        if (_announced) {
            return;
        }

        _announced = true;
        try {
            await _coordinator.OnShellReadyAsync();
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Opening a pending notification failed");
        }
    }
}
