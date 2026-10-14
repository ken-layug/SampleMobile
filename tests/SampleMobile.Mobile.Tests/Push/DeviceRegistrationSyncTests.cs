using System.Net;
using System.Text.Json.Nodes;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Tests.Fakes;

namespace SampleMobile.Mobile.Tests.Push;

/// <summary>Keeping the backend registration in step with the provider address, idempotently and across outages.</summary>
public class DeviceRegistrationSyncTests : IDisposable {
    private readonly MobileTestHost _host = new();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private DeviceRegistrationSync Sync => _host.Get<DeviceRegistrationSync>();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Login_registers_the_stable_installation_with_the_fcm_address() {
        await _host.SignInAsync();

        var body = JsonNode.Parse(_host.Registrations.Single().Body!)!;
        Assert.Equal(_host.InstallationId, (string?)body["installationId"]);
        Assert.Equal("android", (string?)body["platform"]);
        Assert.Equal("fcm", (string?)body["provider"]);
        Assert.Equal("fcm-token-0123456789-A", (string?)body["address"]);
        Assert.NotEqual(_host.InstallationId, (string?)body["address"]);
        Assert.Equal(RegistrationStatus.Registered, Sync.Status);
    }

    [Fact]
    public async Task The_installation_id_survives_token_rotation_and_restarts() {
        await _host.SignInAsync();
        var first = _host.InstallationId;

        await Sync.OnAddressChangedAsync("fcm", "fcm-token-rotated-000000-B", Cancellation);

        var ids = _host.Registrations.Select(request => (string?)JsonNode.Parse(request.Body!)!["installationId"]).Distinct();
        Assert.Equal([first], ids);
    }

    [Fact]
    public async Task Syncing_an_unchanged_registration_again_sends_nothing() {
        await _host.SignInAsync();

        var outcomes = new[] {
            await Sync.SyncAsync(RegistrationSyncReason.Resume, Cancellation),
            await Sync.SyncAsync(RegistrationSyncReason.Connectivity, Cancellation),
            await Sync.SyncAsync(RegistrationSyncReason.AddressChanged, Cancellation)
        };

        Assert.All(outcomes, outcome => Assert.Equal(RegistrationSyncOutcome.Unchanged, outcome));
        Assert.Single(_host.Registrations);
    }

    [Fact]
    public async Task A_rotated_address_is_registered_again() {
        await _host.SignInAsync();

        await Sync.OnAddressChangedAsync("fcm", "fcm-token-rotated-000000-B", Cancellation);

        Assert.Equal(2, _host.Registrations.Count);
        Assert.Contains("fcm-token-rotated-000000-B", _host.Registrations[^1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_address_change_for_an_inactive_provider_is_kept_but_not_sent() {
        await _host.SignInAsync();

        await Sync.OnAddressChangedAsync("onesignal", "other-provider-address-1", Cancellation);

        Assert.Single(_host.Registrations);
    }

    [Fact]
    public async Task A_new_user_on_the_same_installation_always_registers_again() {
        await _host.SignInAsync();
        await _host.Get<ISessionService>().LogoutAsync(Cancellation);
        _host.Fcm.AddressResult = PushAddressResult.Success("fcm-token-0123456789-A");

        await _host.SignInAsync("bob");

        var last = _host.Registrations[^1];
        Assert.Equal("Bearer token-bob-2", last.Authorization);
        Assert.Contains(_host.InstallationId, last.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_temporary_outage_keeps_the_latest_address_and_retries_after_a_bounded_delay() {
        _host.Http.Offline(HttpMethod.Post, "/api/mobile/device-registrations");
        await _host.SignInAsync();

        Assert.Equal(RegistrationStatus.PendingRetry, Sync.Status);
        Assert.True(Sync.HasPendingRetry);
        Assert.Equal("fcm-token-0123456789-A", await _host.SecureStore.GetAsync("push.address.fcm"));

        _host.Http.On(HttpMethod.Post, "/api/mobile/device-registrations", HttpStatusCode.OK, BackendJson.Registration());
        _host.Time.Advance(TimeSpan.FromSeconds(15));
        await Sync.WhenIdleAsync();

        Assert.Equal(RegistrationStatus.Registered, Sync.Status);
        Assert.False(Sync.HasPendingRetry);
        Assert.Equal(2, _host.Registrations.Count);
    }

    [Fact]
    public async Task A_pending_registration_is_sent_when_connectivity_returns() {
        _host.Http.Offline(HttpMethod.Post, "/api/mobile/device-registrations");
        await _host.SignInAsync();
        _host.Http.On(HttpMethod.Post, "/api/mobile/device-registrations", HttpStatusCode.OK, BackendJson.Registration());

        await _host.Get<SessionBootstrapper>().ResumeAsync(RegistrationSyncReason.Connectivity, Cancellation);

        Assert.Equal(RegistrationStatus.Registered, Sync.Status);
    }

    [Fact]
    public async Task A_conflict_is_retried_once() {
        var calls = 0;
        _host.Http.On(HttpMethod.Post, "/api/mobile/device-registrations", _ => ++calls == 1
            ? StubHttpHandler.Problem(HttpStatusCode.Conflict, "Raced")
            : StubHttpHandler.Json(HttpStatusCode.OK, BackendJson.Registration()));

        await _host.SignInAsync();

        Assert.Equal(2, calls);
        Assert.Equal(RegistrationStatus.Registered, Sync.Status);
    }

    [Fact]
    public async Task A_rejected_registration_is_not_retried_in_a_loop() {
        _host.Http.On(HttpMethod.Post, "/api/mobile/device-registrations",
            _ => StubHttpHandler.Problem(HttpStatusCode.BadRequest, "Invalid", """{"provider":["Provider 'fcm' is not supported."]}"""));
        await _host.SignInAsync();

        _host.Time.Advance(TimeSpan.FromMinutes(10));
        await Sync.WhenIdleAsync();

        Assert.Single(_host.Registrations);
        Assert.Equal(RegistrationStatus.Failed, Sync.Status);
        Assert.Contains("not supported", Sync.LastResult?.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_rotations_race_the_latest_address_is_the_one_registered() {
        await _host.SignInAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Http.On(HttpMethod.Post, "/api/mobile/device-registrations", async _ => {
            await release.Task;
            return StubHttpHandler.Json(HttpStatusCode.OK, BackendJson.Registration());
        });

        var first = Sync.OnAddressChangedAsync("fcm", "fcm-token-rotated-000000-B", Cancellation);
        var second = Sync.OnAddressChangedAsync("fcm", "fcm-token-rotated-000000-C", Cancellation);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Contains("fcm-token-rotated-000000-C", _host.Registrations[^1].Body, StringComparison.Ordinal);
        Assert.Equal(RegistrationSyncOutcome.Unchanged, await Sync.SyncAsync(RegistrationSyncReason.Resume, Cancellation));
    }

    [Fact]
    public async Task Registration_results_show_only_a_masked_address() {
        await _host.SignInAsync();

        Assert.Equal("fcm-to…89-A", Sync.LastResult?.MaskedAddress);
        Assert.Equal("fcm-to…89-A", await Sync.GetMaskedAddressAsync());
    }
}
