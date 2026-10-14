using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.Navigation;

/// <summary>
/// Turns "open this notification" requests (notification taps) into navigation once it is safe: a user is signed
/// in and the signed-in shell is on screen. Until then the request waits, which covers a cold start from a tray tap
/// (session restore or login first, then the detail page).
/// </summary>
public sealed class NavigationCoordinator(INavigationService navigation, ISessionStore sessions, ILogger<NavigationCoordinator> logger) {
    private readonly Lock _sync = new();
    private PendingTarget? _pending;
    private bool _shellReady;

    private sealed record PendingTarget(Guid? NotificationId);

    public Guid? PendingNotificationId {
        get {
            lock (_sync) {
                return _pending?.NotificationId;
            }
        }
    }

    public bool HasPending {
        get {
            lock (_sync) {
                return _pending is not null;
            }
        }
    }

    /// <summary>Opens the notification's detail page now, or as soon as the signed-in shell appears.</summary>
    public Task RequestNotificationAsync(Guid notificationId) => RequestAsync(new PendingTarget(notificationId));

    /// <summary>Opens the inbox (used when a tap carried no usable notification id).</summary>
    public Task RequestInboxAsync() => RequestAsync(new PendingTarget(null));

    /// <summary>The signed-in shell is visible; flush any waiting request.</summary>
    public async Task OnShellReadyAsync() {
        PendingTarget? target;
        lock (_sync) {
            _shellReady = true;
            target = _pending;
            _pending = null;
        }

        if (target is not null && sessions.Current is not null) {
            await NavigateAsync(target);
        }
    }

    /// <summary>The shell was replaced (signed out).</summary>
    public void OnShellClosed() {
        lock (_sync) {
            _shellReady = false;
        }
    }

    /// <summary>Drops a waiting request (sign-out, expiry, user change).</summary>
    public void Clear() {
        lock (_sync) {
            _pending = null;
        }
    }

    private async Task RequestAsync(PendingTarget target) {
        bool navigateNow;
        lock (_sync) {
            navigateNow = _shellReady && sessions.Current is not null;
            if (!navigateNow) {
                _pending = target;
            }
        }

        if (navigateNow) {
            await NavigateAsync(target);
        } else {
            logger.LogInformation("Deferring navigation to {Target} until a signed-in shell is shown", target.NotificationId?.ToString() ?? "inbox");
        }
    }

    private Task NavigateAsync(PendingTarget target) =>
        target.NotificationId is { } id ? navigation.OpenNotificationAsync(id) : navigation.OpenInboxAsync();
}
