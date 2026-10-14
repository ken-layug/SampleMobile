using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.ViewModels;

/// <summary>
/// The first screen: restores a stored session (secure storage is asynchronous), then shows the signed-in
/// shell or the login screen. A notification tap that launched the app waits in the navigation coordinator.
/// </summary>
public sealed class StartupViewModel(ISessionService sessions, INavigationService navigation) {
    private bool _started;

    public async Task StartAsync() {
        if (_started) {
            return;
        }

        _started = true;
        if (await sessions.RestoreAsync()) {
            await navigation.ShowSignedInAsync();
        } else {
            await navigation.ShowSignedOutAsync(null);
        }
    }
}
