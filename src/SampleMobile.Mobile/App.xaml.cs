using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Views;

namespace SampleMobile.Mobile;

/// <summary>
/// The MAUI application. It holds no start-up logic: a push service can start the process without any UI,
/// so session restore happens on the first page instead (<see cref="StartupPage"/>).
/// </summary>
public partial class App : Application {
    private readonly IServiceProvider _services;

    public App(IServiceProvider services) {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) {
        var window = new Window(_services.GetRequiredService<StartupPage>()) { Title = "SampleMobile" };
        window.Resumed += OnResumed;
        return window;
    }

    // Back in the foreground: refresh the inbox and finish deferred registration work. This is not polling.
    private async void OnResumed(object? sender, EventArgs e) {
        try {
            await _services.GetRequiredService<SessionBootstrapper>().ResumeAsync(RegistrationSyncReason.Resume);
        } catch (Exception ex) {
            _services.GetRequiredService<ILogger<App>>().LogWarning(ex, "Resume work failed");
        }
    }
}
