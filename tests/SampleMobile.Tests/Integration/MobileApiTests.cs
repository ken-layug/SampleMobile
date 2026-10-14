using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleMobile.Application.Contracts;
using SampleMobile.Infrastructure.Persistence;

namespace SampleMobile.Tests.Integration;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class MobileApiTests(IntegrationFixture fixture) : IntegrationTestBase(fixture) {
    [Fact]
    public async Task Mobile_endpoints_require_a_bearer_token() {
        var client = Fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/mobile/notifications", Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Repeating_a_registration_updates_the_existing_record_and_rotates_the_token() {
        var alice = await Fixture.CreateAliceClientAsync();
        var installationId = Guid.NewGuid().ToString();

        var created = await alice.PostAsJsonAsync("/api/mobile/device-registrations",
            new { installationId, platform = "android", provider = "fcm", address = "fcm-token-first" }, IntegrationFixture.Json, Cancellation);
        var refreshed = await alice.PostAsJsonAsync("/api/mobile/device-registrations",
            new { installationId, platform = "android", provider = "fcm", address = "fcm-token-rotated" }, IntegrationFixture.Json, Cancellation);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(IntegrationFixture.Json, Cancellation);
        var second = await refreshed.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(IntegrationFixture.Json, Cancellation);
        Assert.Equal(first!.Id, second!.Id);

        await using var scope = Fixture.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeviceRegistrations
            .Where(registration => registration.InstallationId == installationId)
            .ToListAsync(Cancellation);
        var row = Assert.Single(rows);
        Assert.Equal("fcm-token-rotated", row.Address);
        Assert.Equal(await UserIdAsync(IntegrationFixture.AliceUserName), row.UserId);
    }

    [Fact]
    public async Task The_owner_always_comes_from_the_token_even_if_the_body_names_someone_else() {
        var alice = await Fixture.CreateAliceClientAsync();
        var bobId = await UserIdAsync(IntegrationFixture.BobUserName);
        var installationId = Guid.NewGuid().ToString();

        var response = await alice.PostAsJsonAsync("/api/mobile/device-registrations",
            new { installationId, platform = "android", provider = "simulated", address = "sim-owner-test", userId = bobId },
            IntegrationFixture.Json, Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = Fixture.CreateScope();
        var owner = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeviceRegistrations
            .Where(registration => registration.InstallationId == installationId)
            .Select(registration => registration.UserId)
            .SingleAsync(Cancellation);
        Assert.Equal(await UserIdAsync(IntegrationFixture.AliceUserName), owner);
    }

    [Fact]
    public async Task A_user_cannot_unregister_another_users_device() {
        var alice = await Fixture.CreateAliceClientAsync();
        var bob = await Fixture.CreateBobClientAsync();
        var installationId = Guid.NewGuid().ToString();
        await alice.PostAsJsonAsync("/api/mobile/device-registrations",
            new { installationId, platform = "android", provider = "simulated", address = $"sim-{installationId}" }, IntegrationFixture.Json, Cancellation);

        var response = await bob.DeleteAsync($"/api/mobile/device-registrations/{installationId}?provider=simulated", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var aliceDevices = await alice.GetFromJsonAsync<List<DeviceRegistrationResponse>>("/api/mobile/device-registrations", IntegrationFixture.Json, Cancellation);
        Assert.True(aliceDevices!.Single(device => device.InstallationId == installationId).Enabled);
    }

    [Fact]
    public async Task A_user_cannot_read_or_mark_another_users_notification() {
        var alice = await Fixture.CreateAliceClientAsync();
        var bob = await Fixture.CreateBobClientAsync();
        var published = await PublishAsync(await UserIdAsync(IntegrationFixture.AliceUserName));

        var read = await bob.GetAsync($"/api/mobile/notifications/{published.NotificationId}", Cancellation);
        var mark = await bob.PostAsync($"/api/mobile/notifications/{published.NotificationId}/read", null, Cancellation);
        var bobInbox = await bob.GetFromJsonAsync<NotificationListResponse>("/api/mobile/notifications?pageSize=100", IntegrationFixture.Json, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mark.StatusCode);
        Assert.DoesNotContain(bobInbox!.Items, item => item.Id == published.NotificationId);
        var aliceView = await alice.GetFromJsonAsync<NotificationResponse>($"/api/mobile/notifications/{published.NotificationId}", IntegrationFixture.Json, Cancellation);
        Assert.Null(aliceView!.ReadAt);
    }

    [Fact]
    public async Task Marking_a_notification_read_is_idempotent() {
        var alice = await Fixture.CreateAliceClientAsync();
        var published = await PublishAsync(await UserIdAsync(IntegrationFixture.AliceUserName));

        var first = await alice.PostAsync($"/api/mobile/notifications/{published.NotificationId}/read", null, Cancellation);
        var second = await alice.PostAsync($"/api/mobile/notifications/{published.NotificationId}/read", null, Cancellation);

        var firstBody = await first.Content.ReadFromJsonAsync<NotificationResponse>(IntegrationFixture.Json, Cancellation);
        var secondBody = await second.Content.ReadFromJsonAsync<NotificationResponse>(IntegrationFixture.Json, Cancellation);
        Assert.NotNull(firstBody!.ReadAt);
        Assert.Equal(firstBody.ReadAt, secondBody!.ReadAt);
    }

    [Fact]
    public async Task Unread_notifications_serialize_read_at_as_null() {
        var alice = await Fixture.CreateAliceClientAsync();
        var published = await PublishAsync(await UserIdAsync(IntegrationFixture.AliceUserName));

        var json = await alice.GetStringAsync($"/api/mobile/notifications/{published.NotificationId}", Cancellation);

        Assert.Contains("\"readAt\":null", json);
        Assert.Contains($"\"id\":\"{published.NotificationId}\"", json);
    }
}
