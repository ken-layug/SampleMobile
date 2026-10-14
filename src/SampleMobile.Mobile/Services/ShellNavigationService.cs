using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Views;

namespace SampleMobile.Mobile.Services;

/// <summary>
/// Swaps the window between the login page and a fresh <see cref="AppShell"/>, and navigates inside the shell.
/// Everything runs on the UI thread.
/// </summary>
public sealed class ShellNavigationService(IServiceProvider services) : INavigationService {
    public Task ShowSignedInAsync() => OnMainThreadAsync(() => {
        if (CurrentWindow is { } window) {
            window.Page = services.GetRequiredService<AppShell>();
        }

        return Task.CompletedTask;
    });

    public Task ShowSignedOutAsync(string? message) => OnMainThreadAsync(() => {
        if (CurrentWindow is { } window) {
            var login = services.GetRequiredService<LoginPage>();
            login.ShowStatus(message);
            window.Page = login;
        }

        return Task.CompletedTask;
    });

    public Task OpenNotificationAsync(Guid notificationId) => OnMainThreadAsync(async () => {
        if (CurrentShell is { } shell) {
            await shell.GoToAsync($"{Routes.Notification}?id={notificationId:D}");
        }
    });

    public Task OpenInboxAsync() => OnMainThreadAsync(async () => {
        if (CurrentShell is { } shell) {
            await shell.GoToAsync($"//{Routes.Inbox}");
        }
    });

    private static Window? CurrentWindow => Application.Current?.Windows.FirstOrDefault();

    private static Shell? CurrentShell => CurrentWindow?.Page as Shell;

    private static Task OnMainThreadAsync(Func<Task> action) => MainThread.InvokeOnMainThreadAsync(action);
}
