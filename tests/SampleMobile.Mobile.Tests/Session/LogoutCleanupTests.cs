using System.Net;
using NSubstitute;
using SampleMobile.Mobile.Core.Navigation;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Tests.Fakes;

namespace SampleMobile.Mobile.Tests.Session;

/// <summary>Sign-out removes the device association, also when offline, and leaves nothing of the user behind.</summary>
public class LogoutCleanupTests : IDisposable {
    private readonly MobileTestHost _host = new();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string DeletePath => $"/api/mobile/device-registrations/{_host.InstallationId}";

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Logout_unregisters_with_the_signed_out_users_token_then_resets_the_provider() {
        await _host.SignInAsync();

        await _host.Get<ISessionService>().LogoutAsync(Cancellation);

        var delete = _host.Http.RequestsTo(HttpMethod.Delete, DeletePath).Single();
        Assert.Equal("Bearer token-alice-1", delete.Authorization);
        Assert.Equal("?provider=fcm", delete.Query);
        Assert.Equal("reset", _host.Fcm.Calls[^1]);
        Assert.Empty(await _host.Get<DeviceRegistrationSync>().GetPendingCleanupAsync());
        Assert.Null(await _host.SecureStore.GetAsync("push.address.fcm"));
    }

    [Fact]
    public async Task Logout_clears_the_token_inbox_pending_navigation_and_tray() {
        await _host.SignInAsync();
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(Guid.NewGuid())));
        await _host.Get<INotificationInboxService>().RefreshAsync(Cancellation);
        await _host.Get<NavigationCoordinator>().RequestNotificationAsync(Guid.NewGuid());

        await _host.Get<ISessionService>().LogoutAsync(Cancellation);

        Assert.Null(_host.Get<ISessionStore>().Current);
        Assert.False(_host.SecureStore.Values.ContainsKey(SessionStore.StorageKey));
        Assert.Empty(_host.Get<INotificationInboxService>().Items);
        Assert.False(_host.Get<NavigationCoordinator>().HasPending);
        _host.Presenter.Received(1).CancelAll();
        await _host.Navigation.Received(1).ShowSignedOutAsync(null);
    }

    [Fact]
    public async Task No_registration_is_sent_after_logout_even_with_a_retry_scheduled() {
        _host.Http.Offline(HttpMethod.Post, "/api/mobile/device-registrations");
        await _host.SignInAsync();
        _host.Http.On(HttpMethod.Post, "/api/mobile/device-registrations", HttpStatusCode.OK, BackendJson.Registration());

        await _host.Get<ISessionService>().LogoutAsync(Cancellation);
        _host.Time.Advance(TimeSpan.FromMinutes(10));
        await _host.Get<DeviceRegistrationSync>().WhenIdleAsync();

        Assert.Single(_host.Registrations);
        var last = _host.Http.Requests.Last(request => request.Path.StartsWith("/api/mobile/device-registrations", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Delete, last.Method);
    }

    [Fact]
    public async Task An_offline_logout_keeps_the_cleanup_and_finishes_it_when_back_online() {
        await _host.SignInAsync();
        _host.Http.Offline(HttpMethod.Delete, DeletePath);
        _host.Fcm.ResetSucceeds = false;

        await _host.Get<ISessionService>().LogoutAsync(Cancellation);

        var pending = Assert.Single(await _host.Get<DeviceRegistrationSync>().GetPendingCleanupAsync());
        Assert.True(pending is { DeletePending: true, ResetPending: true, Provider: "fcm", AccessToken: "token-alice-1" });

        _host.Http.On(HttpMethod.Delete, DeletePath, HttpStatusCode.NoContent);
        _host.Fcm.ResetSucceeds = true;
        await _host.Get<SessionBootstrapper>().ResumeAsync(RegistrationSyncReason.Connectivity, Cancellation);

        Assert.Empty(await _host.Get<DeviceRegistrationSync>().GetPendingCleanupAsync());
        Assert.Equal("Bearer token-alice-1", _host.Http.RequestsTo(HttpMethod.Delete, DeletePath)[^1].Authorization);
    }

    [Fact]
    public async Task Once_the_saved_token_expired_the_delete_is_dropped_but_the_provider_reset_still_runs() {
        await _host.SignInAsync();
        _host.Http.Offline(HttpMethod.Delete, DeletePath);
        _host.Fcm.ResetSucceeds = false;
        await _host.Get<ISessionService>().LogoutAsync(Cancellation);
        var deletesBefore = _host.Http.RequestsTo(HttpMethod.Delete, DeletePath).Count;

        _host.Time.Advance(TimeSpan.FromHours(9));
        _host.Fcm.ResetSucceeds = true;
        await _host.Get<DeviceRegistrationSync>().ProcessPendingCleanupAsync(beforeRegistration: false, Cancellation);

        Assert.Equal(deletesBefore, _host.Http.RequestsTo(HttpMethod.Delete, DeletePath).Count);
        Assert.Equal("reset", _host.Fcm.Calls[^1]);
        Assert.Empty(await _host.Get<DeviceRegistrationSync>().GetPendingCleanupAsync());
    }

    [Fact]
    public async Task A_pending_provider_reset_runs_before_the_next_login_reads_the_address() {
        await _host.SignInAsync();
        _host.Http.Offline(HttpMethod.Delete, DeletePath);
        _host.Fcm.ResetSucceeds = false;
        await _host.Get<ISessionService>().LogoutAsync(Cancellation);
        _host.Http.On(HttpMethod.Delete, DeletePath, HttpStatusCode.NoContent);
        _host.Fcm.ResetSucceeds = true;
        var callsBefore = _host.Fcm.Calls.Count;

        await _host.SignInAsync("bob");

        var calls = _host.Fcm.Calls.Skip(callsBefore).ToList();
        Assert.True(calls.IndexOf("reset") < calls.IndexOf("address"), string.Join(",", calls));
        Assert.Empty(await _host.Get<DeviceRegistrationSync>().GetPendingCleanupAsync());
        Assert.Equal("Bearer token-bob-2", _host.Registrations[^1].Authorization);
    }

    [Fact]
    public async Task A_successful_registration_supersedes_a_pending_cleanup_for_the_same_installation() {
        await _host.SignInAsync();
        _host.Http.Offline(HttpMethod.Delete, DeletePath);
        _host.Fcm.ResetSucceeds = false;
        await _host.Get<ISessionService>().LogoutAsync(Cancellation);

        // Still offline for DELETE and the reset, but the new registration gets through.
        await _host.SignInAsync();

        Assert.Empty(await _host.Get<DeviceRegistrationSync>().GetPendingCleanupAsync());
        Assert.Equal(RegistrationStatus.Registered, _host.Get<DeviceRegistrationSync>().Status);
    }

    [Fact]
    public async Task While_signed_in_a_leftover_reset_never_deletes_the_active_users_address() {
        await _host.SignInAsync();
        _host.Http.Offline(HttpMethod.Delete, DeletePath);
        _host.Fcm.ResetSucceeds = false;
        await _host.Get<ISessionService>().LogoutAsync(Cancellation);
        _host.Http.Offline(HttpMethod.Post, "/api/mobile/device-registrations");
        await _host.SignInAsync();
        var resetsBefore = _host.Fcm.Calls.Count(call => call.StartsWith("reset", StringComparison.Ordinal));

        _host.Fcm.ResetSucceeds = true;
        await _host.Get<DeviceRegistrationSync>().ProcessPendingCleanupAsync(beforeRegistration: false, Cancellation);

        Assert.Equal(resetsBefore, _host.Fcm.Calls.Count(call => call.StartsWith("reset", StringComparison.Ordinal)));
        Assert.NotNull(await _host.SecureStore.GetAsync("push.address.fcm"));
    }
}
