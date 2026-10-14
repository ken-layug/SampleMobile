using System.Net;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.ViewModels;
using SampleMobile.Mobile.Tests.Fakes;

namespace SampleMobile.Mobile.Tests.Notifications;

/// <summary>The app-owned inbox: server-authoritative, keyed by backend id, explicit loading and error states.</summary>
public class NotificationInboxServiceTests : IDisposable {
    private readonly MobileTestHost _host = new();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private INotificationInboxService Inbox => _host.Get<INotificationInboxService>();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_refresh_replaces_the_list_with_the_servers_first_page_newest_first() {
        await _host.SignInAsync();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(2, 7,
            BackendJson.Notification(older, createdAt: MobileTestHost.Start),
            BackendJson.Notification(newer, createdAt: MobileTestHost.Start.AddMinutes(5))));

        await Inbox.RefreshAsync(Cancellation);

        Assert.Equal([newer, older], Inbox.Items.Select(item => item.Id));
        Assert.Equal((2, 7), (Inbox.UnreadCount, Inbox.TotalCount));
        Assert.Equal(MobileTestHost.Start, Inbox.LastRefreshedAt);
        Assert.Equal("?page=1&pageSize=50&unreadOnly=false", _host.Http.Requests[^1].Query);
    }

    [Fact]
    public async Task Marking_a_notification_read_updates_the_item_and_the_unread_count() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(id)));
        _host.Http.On(HttpMethod.Post, $"/api/mobile/notifications/{id}/read", HttpStatusCode.OK, BackendJson.Notification(id, readAt: MobileTestHost.Start.AddMinutes(1)));
        await Inbox.RefreshAsync(Cancellation);

        var result = await Inbox.MarkAsReadAsync(id, Cancellation);

        Assert.True(result.Succeeded);
        Assert.True(Inbox.Items.Single().IsRead);
        Assert.Equal(0, Inbox.UnreadCount);
    }

    [Fact]
    public async Task Marking_read_twice_is_idempotent_and_keeps_the_first_read_time() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        var readAt = MobileTestHost.Start.AddMinutes(1);
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(id)));
        _host.Http.On(HttpMethod.Post, $"/api/mobile/notifications/{id}/read", HttpStatusCode.OK, BackendJson.Notification(id, readAt: readAt));
        await Inbox.RefreshAsync(Cancellation);

        await Inbox.MarkAsReadAsync(id, Cancellation);
        await Inbox.MarkAsReadAsync(id, Cancellation);

        Assert.Equal(readAt, Inbox.Items.Single().ReadAt);
        Assert.Equal(0, Inbox.UnreadCount);
    }

    [Fact]
    public async Task Concurrent_refreshes_share_one_request() {
        await _host.SignInAsync();
        var before = _host.Http.RequestsTo(HttpMethod.Get, "/api/mobile/notifications").Count;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", async _ => {
            await release.Task;
            return StubHttpHandler.Json(HttpStatusCode.OK, BackendJson.List());
        });

        var first = Inbox.RefreshAsync(Cancellation);
        var second = Inbox.RefreshAsync(Cancellation);
        Assert.True(Inbox.IsRefreshing);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(before + 1, _host.Http.RequestsTo(HttpMethod.Get, "/api/mobile/notifications").Count);
        Assert.False(Inbox.IsRefreshing);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_last_list_and_reports_the_error() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(id)));
        await Inbox.RefreshAsync(Cancellation);
        _host.Http.Offline(HttpMethod.Get, "/api/mobile/notifications");

        var result = await Inbox.RefreshAsync(Cancellation);

        Assert.Equal(ApiFailureKind.Network, result.Failure);
        Assert.Equal(ApiFailureKind.Network, Inbox.LastError?.Failure);
        Assert.Equal(id, Inbox.Items.Single().Id);
    }

    [Fact]
    public async Task The_inbox_view_model_shows_loading_empty_and_error_states() {
        await _host.SignInAsync();
        _host.Get<INotificationInboxService>().Clear();
        var viewModel = _host.Get<InboxViewModel>();

        viewModel.Activate();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);

        _host.Http.Offline(HttpMethod.Get, "/api/mobile/notifications");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsRefreshing);
        Assert.False(viewModel.IsLoading);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Opening_a_notification_fetches_it_from_the_api_and_marks_it_read() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, $"/api/mobile/notifications/{id}", HttpStatusCode.OK, BackendJson.Notification(id));
        _host.Http.On(HttpMethod.Post, $"/api/mobile/notifications/{id}/read", HttpStatusCode.OK, BackendJson.Notification(id, readAt: MobileTestHost.Start));
        var detail = _host.Get<NotificationDetailViewModel>();

        detail.SetNotificationId(id.ToString());
        await detail.LoadAsync();

        Assert.Equal("Warning alert", detail.Title);
        Assert.True(detail.IsRead);
        Assert.Single(_host.Http.RequestsTo(HttpMethod.Post, $"/api/mobile/notifications/{id}/read"));
    }

    [Fact]
    public async Task A_detail_link_with_an_invalid_or_unknown_id_shows_not_available() {
        await _host.SignInAsync();
        var unknown = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, $"/api/mobile/notifications/{unknown}", _ => StubHttpHandler.Problem(HttpStatusCode.NotFound, "Notification not found."));
        var invalid = _host.Get<NotificationDetailViewModel>();
        var missing = _host.Get<NotificationDetailViewModel>();

        invalid.SetNotificationId("definitely-not-a-guid");
        await invalid.LoadAsync();
        missing.SetNotificationId(unknown.ToString());
        await missing.LoadAsync();

        Assert.True(invalid.IsUnavailable);
        Assert.True(missing.IsUnavailable);
        Assert.Empty(_host.Http.RequestsTo(HttpMethod.Post, $"/api/mobile/notifications/{unknown}/read"));
    }
}
