using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Api.Contracts;
using SampleMobile.Mobile.Core.Navigation;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Core.Session;

/// <summary>Outcome of a login attempt, with a message fit for the login screen.</summary>
public sealed record LoginResult(bool Succeeded, string? Error) {
    public static readonly LoginResult Success = new(true, null);

    public static LoginResult Fail(string error) => new(false, error);
}

/// <summary>Sign-in, session restore and sign-out, orchestrating everything that depends on who is signed in.</summary>
public interface ISessionService {
    /// <summary>Restores a stored session at launch. Returns false when the user must sign in.</summary>
    Task<bool> RestoreAsync(CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class SessionService : ISessionService {
    private const string ExpiredMessage = "Your session expired. Sign in again.";

    private readonly ISessionStore _sessions;
    private readonly IAuthApiClient _auth;
    private readonly IBackendSettings _backend;
    private readonly SessionBootstrapper _bootstrapper;
    private readonly DeviceRegistrationSync _registration;
    private readonly INotificationInboxService _inbox;
    private readonly ILocalNotificationPresenter _presenter;
    private readonly NavigationCoordinator _coordinator;
    private readonly PushEventProcessor _pushEvents;
    private readonly INavigationService _navigation;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        ISessionStore sessions,
        IAuthApiClient auth,
        IBackendSettings backend,
        SessionBootstrapper bootstrapper,
        DeviceRegistrationSync registration,
        INotificationInboxService inbox,
        ILocalNotificationPresenter presenter,
        NavigationCoordinator coordinator,
        PushEventProcessor pushEvents,
        INavigationService navigation,
        ILogger<SessionService> logger) {
        _sessions = sessions;
        _auth = auth;
        _backend = backend;
        _bootstrapper = bootstrapper;
        _registration = registration;
        _inbox = inbox;
        _presenter = presenter;
        _coordinator = coordinator;
        _pushEvents = pushEvents;
        _navigation = navigation;
        _logger = logger;

        _sessions.Changed += OnSessionChanged;
    }

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default) {
        if (await _sessions.GetActiveAsync(cancellationToken) is null) {
            // Signed out: only leftover sign-out cleanup can run.
            _ = _registration.ProcessPendingCleanupAsync(beforeRegistration: false, CancellationToken.None);
            return false;
        }

        _ = _bootstrapper.RunAsync(BootstrapReason.AppStart);
        return true;
    }

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default) {
        userName = userName.Trim();
        if (userName.Length == 0 || password.Length == 0) {
            return LoginResult.Fail("Enter a user name and a password.");
        }

        if (_backend.BaseUri is null) {
            return LoginResult.Fail("Enter the backend URL first.");
        }

        var result = await _auth.LoginAsync(new LoginRequest(userName, password), cancellationToken);
        if (result.Value is not { } token) {
            return LoginResult.Fail(Describe(result));
        }

        // Nothing of a previous session may leak into this one (a pending tap navigation is kept on purpose).
        _inbox.Clear();
        _pushEvents.Reset();

        await _sessions.SignInAsync(new UserSession(userName, token.AccessToken, token.ExpiresAt));
        _logger.LogInformation("Signed in as {UserName}", userName);

        _ = _bootstrapper.RunAsync(BootstrapReason.Login);
        await _navigation.ShowSignedInAsync();
        return LoginResult.Success;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default) {
        var session = await _sessions.GetActiveAsync(cancellationToken);
        if (session is not null) {
            // Record the unregister (with this token) durably before the token is discarded.
            await _registration.BeginLogoutAsync(session, cancellationToken);
        }

        await _sessions.SignOutAsync();
        ClearUserState();
        await _navigation.ShowSignedOutAsync(null);

        // DELETE with the signed-out user's token, then reset the provider address. Offline, it stays pending.
        await _registration.ProcessPendingCleanupAsync(beforeRegistration: false, cancellationToken);
    }

    private async void OnSessionChanged(object? sender, SessionChangedEventArgs e) {
        if (e.Kind != SessionChangeKind.Expired) {
            return;
        }

        try {
            // No DELETE is possible without a valid token. The registration stays until the next login moves or refreshes it.
            ClearUserState();
            _registration.OnSessionEnded();
            await _navigation.ShowSignedOutAsync(ExpiredMessage);
        } catch (Exception ex) {
            _logger.LogError(ex, "Handling session expiry failed");
        }
    }

    private void ClearUserState() {
        _inbox.Clear();
        _coordinator.Clear();
        _pushEvents.Reset();
        _presenter.CancelAll();
    }

    private static string Describe(ApiResult result) => result.Failure switch {
        ApiFailureKind.Unauthorized => "Invalid user name or password.",
        ApiFailureKind.Invalid => result.Errors.Count > 0 ? string.Join(" ", result.Errors.SelectMany(error => error.Value)) : "The request was rejected.",
        ApiFailureKind.Network => $"Can't reach the backend. Check the URL and that the server is running. ({result.Message})",
        ApiFailureKind.Server => $"The backend had a problem ({result.StatusCode}). Try again.",
        ApiFailureKind.NotConfigured => "Enter the backend URL first.",
        _ => result.Describe()
    };
}
