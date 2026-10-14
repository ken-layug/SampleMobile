using System.Net;
using System.Text.Json.Nodes;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Tests.Fakes;

namespace SampleMobile.Mobile.Tests.Push;

/// <summary>Without Firebase the app still signs in, registers as the simulated provider and syncs the inbox.</summary>
public class InboxOnlyModeTests {
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task When_firebase_is_not_configured_the_app_falls_back_to_inbox_only() {
        using var host = new MobileTestHost();
        host.Fcm.Status = new PushTransportStatus(PushTransportState.NotConfigured, "google-services.json is missing");

        await host.SignInAsync();

        var selector = host.Get<PushTransportSelector>();
        Assert.Equal(InboxOnlyPushTransport.Id, selector.Active.ProviderId);
        Assert.Equal(PushTransportState.NotConfigured, selector.PreferredStatus.State);
        Assert.DoesNotContain("address", host.Fcm.Calls);
        AssertRegisteredAsSimulated(host);
    }

    [Fact]
    public async Task Choosing_inbox_only_at_login_never_touches_the_real_transport() {
        using var host = new MobileTestHost();
        host.Get<PushTransportSelector>().Mode = PushMode.InboxOnly;

        await host.SignInAsync();

        Assert.Empty(host.Fcm.Calls);
        AssertRegisteredAsSimulated(host);
    }

    [Fact]
    public async Task An_unavailable_provider_does_not_break_sign_in_or_the_inbox() {
        using var host = new MobileTestHost();
        host.Fcm.Status = new PushTransportStatus(PushTransportState.Unavailable, "Google Play services missing");
        var id = Guid.NewGuid();
        host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(id)));

        await host.SignInAsync();

        Assert.Equal(id, host.Get<INotificationInboxService>().Items.Single().Id);
    }

    [Fact]
    public async Task A_build_without_any_push_transport_still_has_a_working_inbox() {
        using var host = new MobileTestHost(withPushTransport: false);
        var id = Guid.NewGuid();
        host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(id)));
        host.Http.On(HttpMethod.Post, $"/api/mobile/notifications/{id}/read", HttpStatusCode.OK, BackendJson.Notification(id, readAt: MobileTestHost.Start));

        await host.SignInAsync();
        var inbox = host.Get<INotificationInboxService>();
        await inbox.MarkAsReadAsync(id, Cancellation);

        Assert.True(inbox.Items.Single().IsRead);
        AssertRegisteredAsSimulated(host);
    }

    [Fact]
    public async Task A_provider_address_failure_still_registers_nothing_bogus_and_keeps_the_inbox() {
        using var host = new MobileTestHost();
        host.Fcm.AddressResult = PushAddressResult.Transient("SERVICE_NOT_AVAILABLE");

        await host.SignInAsync();

        Assert.Empty(host.Registrations);
        Assert.True(host.Get<INotificationInboxService>().HasLoaded);
    }

    private static void AssertRegisteredAsSimulated(MobileTestHost host) {
        var body = JsonNode.Parse(host.Registrations.Single().Body!)!;
        Assert.Equal("simulated", (string?)body["provider"]);
        Assert.Equal($"inbox-only:{host.InstallationId}", (string?)body["address"]);
    }
}
