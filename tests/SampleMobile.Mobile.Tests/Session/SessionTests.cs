using System.Net;
using NSubstitute;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Navigation;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Tests.Fakes;

namespace SampleMobile.Mobile.Tests.Session;

/// <summary>Login, restore, expiry and the 401 handling around the bearer token.</summary>
public class SessionTests : IDisposable {
    private readonly MobileTestHost _host = new();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_successful_login_stores_the_token_securely_and_shows_the_signed_in_shell() {
        await _host.SignInAsync();

        var session = _host.Get<ISessionStore>().Current;
        Assert.Equal(("alice", "token-alice-1"), (session?.UserName, session?.AccessToken));
        Assert.Contains("token-alice-1", _host.SecureStore.Values[SessionStore.StorageKey], StringComparison.Ordinal);
        Assert.DoesNotContain(_host.Preferences.Values.Values, value => value.Contains("token-alice", StringComparison.Ordinal));
        await _host.Navigation.Received(1).ShowSignedInAsync();
    }

    [Fact]
    public async Task Wrong_credentials_give_one_message_and_leave_the_user_signed_out() {
        _host.Http.On(HttpMethod.Post, "/api/mobile/auth/login", _ => StubHttpHandler.Problem(HttpStatusCode.Unauthorized, "Invalid user name or password."));

        var result = await _host.Get<ISessionService>().LoginAsync("alice", "wrong", Cancellation);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid user name or password.", result.Error);
        Assert.Null(_host.Get<ISessionStore>().Current);
        await _host.Navigation.DidNotReceive().ShowSignedInAsync();
    }

    [Fact]
    public async Task A_401_on_a_protected_call_expires_the_session_and_returns_to_login() {
        await _host.SignInAsync();
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", _ => StubHttpHandler.Problem(HttpStatusCode.Unauthorized, "Expired"));

        await _host.Get<INotificationApiClient>().GetNotificationsAsync(cancellationToken: Cancellation);

        Assert.Null(_host.Get<ISessionStore>().Current);
        Assert.False(_host.SecureStore.Values.ContainsKey(SessionStore.StorageKey));
        await _host.Navigation.Received(1).ShowSignedOutAsync(Arg.Is<string?>(message => message!.Contains("expired", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task A_late_401_for_an_older_token_does_not_sign_out_a_newer_session() {
        await _host.SignInAsync();
        var store = _host.Get<ISessionStore>();

        await _host.Get<ISessionService>().LogoutAsync(Cancellation);
        await _host.SignInAsync("bob");
        var expired = await store.ExpireIfCurrentAsync("token-alice-1");

        Assert.False(expired);
        Assert.Equal("bob", store.Current?.UserName);
    }

    [Fact]
    public async Task A_stored_session_is_restored_lazily_for_example_in_a_process_started_by_a_push() {
        await _host.SignInAsync();
        var json = _host.SecureStore.Values[SessionStore.StorageKey];
        using var coldStart = new MobileTestHost();
        await coldStart.SecureStore.SetAsync(SessionStore.StorageKey, json);

        var session = await coldStart.Get<ISessionStore>().GetActiveAsync(Cancellation);

        Assert.Equal("alice", session?.UserName);
    }

    [Fact]
    public async Task A_stored_session_past_its_expiry_counts_as_signed_out() {
        await _host.SignInAsync();

        _host.Time.Advance(TimeSpan.FromHours(8));
        var restored = await _host.Get<ISessionService>().RestoreAsync(Cancellation);

        Assert.False(restored);
        Assert.False(_host.SecureStore.Values.ContainsKey(SessionStore.StorageKey));
    }

    [Fact]
    public async Task An_unreadable_stored_session_is_discarded() {
        await _host.SecureStore.SetAsync(SessionStore.StorageKey, "{not json");

        var session = await _host.Get<ISessionStore>().GetActiveAsync(Cancellation);

        Assert.Null(session);
        Assert.False(_host.SecureStore.Values.ContainsKey(SessionStore.StorageKey));
    }

    [Fact]
    public async Task Session_expiry_clears_the_inbox_pending_navigation_and_tray() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(id)));
        var inbox = _host.Get<INotificationInboxService>();
        await inbox.RefreshAsync(Cancellation);
        var coordinator = _host.Get<NavigationCoordinator>();
        await coordinator.RequestNotificationAsync(Guid.NewGuid());

        await _host.Get<ISessionStore>().ExpireIfCurrentAsync("token-alice-1");

        Assert.Empty(inbox.Items);
        Assert.False(coordinator.HasPending);
        _host.Presenter.Received(1).CancelAll();
    }
}
