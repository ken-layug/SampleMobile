using System.Net;
using NSubstitute;
using SampleMobile.Mobile.Core.Navigation;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Tests.Fakes;

namespace SampleMobile.Mobile.Tests.Push;

/// <summary>A push is a hint: fetch by backend id, update the inbox, show it once, ignore what isn't ours.</summary>
public class PushEventProcessorTests : IDisposable {
    private readonly MobileTestHost _host = new();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private PushEventProcessor Processor => _host.Get<PushEventProcessor>();

    private INotificationInboxService Inbox => _host.Get<INotificationInboxService>();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_push_fetches_the_notification_by_id_updates_the_inbox_and_shows_it_once() {
        await _host.SignInAsync();
        var id = ServeNotification();

        var outcome = await Processor.HandleMessageAsync(Push(id), Cancellation);

        Assert.Equal(PushMessageOutcome.Presented, outcome);
        Assert.Single(_host.Http.RequestsTo(HttpMethod.Get, $"/api/mobile/notifications/{id}"));
        Assert.Equal(id, Inbox.Items.Single().Id);
        Assert.Equal(1, Inbox.UnreadCount);
        _host.Presenter.Received(1).Show(Arg.Is<InboxNotification>(notification => notification.Id == id && notification.Title == "Warning alert"));
    }

    [Fact]
    public async Task A_duplicate_delivery_of_the_same_push_is_handled_once() {
        await _host.SignInAsync();
        var id = ServeNotification();

        await Processor.HandleMessageAsync(Push(id), Cancellation);
        var second = await Processor.HandleMessageAsync(Push(id), Cancellation);

        Assert.Equal(PushMessageOutcome.Duplicate, second);
        Assert.Single(Inbox.Items);
        _host.Presenter.Received(1).Show(Arg.Any<InboxNotification>());
    }

    [Fact]
    public async Task A_push_followed_by_a_refresh_leaves_one_item_per_backend_id() {
        await _host.SignInAsync();
        var id = ServeNotification();
        _host.Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List(1, null, BackendJson.Notification(id)));

        await Processor.HandleMessageAsync(Push(id), Cancellation);
        await Inbox.RefreshAsync(Cancellation);

        Assert.Equal([id], Inbox.Items.Select(item => item.Id));
        Assert.Equal(1, Inbox.UnreadCount);
    }

    [Fact]
    public async Task Events_raised_by_the_transport_reach_the_inbox() {
        await _host.SignInAsync();
        var id = ServeNotification();

        _host.Fcm.RaiseMessage(Push(id));
        await WaitUntilAsync(() => Inbox.Items.Count == 1);

        Assert.Equal(id, Inbox.Items.Single().Id);
    }

    [Fact]
    public async Task A_push_without_a_notification_id_refreshes_the_list_instead() {
        await _host.SignInAsync();
        var listCallsBefore = _host.Http.RequestsTo(HttpMethod.Get, "/api/mobile/notifications").Count;

        var outcome = await Processor.HandleMessageAsync(Push(null), Cancellation);

        Assert.Equal(PushMessageOutcome.Refreshed, outcome);
        Assert.Equal(listCallsBefore + 1, _host.Http.RequestsTo(HttpMethod.Get, "/api/mobile/notifications").Count);
        _host.Presenter.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public async Task A_push_with_a_malformed_notification_id_never_fetches_by_id() {
        await _host.SignInAsync();

        var outcome = await Processor.HandleMessageAsync(Push(null, rawId: "../../admin"), Cancellation);

        Assert.Equal(PushMessageOutcome.Refreshed, outcome);
        Assert.DoesNotContain(_host.Http.Requests, request => request.Path.Contains("admin", StringComparison.Ordinal));
        _host.Presenter.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public async Task A_push_for_a_notification_this_user_cannot_see_is_ignored() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, $"/api/mobile/notifications/{id}", _ => StubHttpHandler.Problem(HttpStatusCode.NotFound, "Notification not found."));

        var outcome = await Processor.HandleMessageAsync(Push(id), Cancellation);

        Assert.Equal(PushMessageOutcome.NotFound, outcome);
        Assert.Empty(Inbox.Items);
        _host.Presenter.DidNotReceiveWithAnyArgs().Show(default!);
        _host.Presenter.DidNotReceiveWithAnyArgs().ShowGeneric(default);
    }

    [Fact]
    public async Task While_offline_a_push_shows_a_content_free_notice_and_a_redelivery_fetches_again() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        _host.Http.Offline(HttpMethod.Get, $"/api/mobile/notifications/{id}");

        var offline = await Processor.HandleMessageAsync(Push(id), Cancellation);
        _host.Http.On(HttpMethod.Get, $"/api/mobile/notifications/{id}", HttpStatusCode.OK, BackendJson.Notification(id));
        var redelivered = await Processor.HandleMessageAsync(Push(id), Cancellation);

        Assert.Equal(PushMessageOutcome.PresentedGeneric, offline);
        _host.Presenter.Received(1).ShowGeneric(id);
        Assert.Equal(PushMessageOutcome.Presented, redelivered);
        _host.Presenter.Received(1).Show(Arg.Is<InboxNotification>(notification => notification.Id == id));
    }

    [Fact]
    public async Task A_push_the_os_already_displayed_only_updates_the_inbox() {
        await _host.SignInAsync();
        var id = ServeNotification();

        var outcome = await Processor.HandleMessageAsync(Push(id, hasDisplayedNotification: true), Cancellation);

        Assert.Equal(PushMessageOutcome.Updated, outcome);
        Assert.Single(Inbox.Items);
        _host.Presenter.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public async Task A_push_for_an_already_read_notification_is_not_shown_again() {
        await _host.SignInAsync();
        var id = ServeNotification(readAt: MobileTestHost.Start);

        var outcome = await Processor.HandleMessageAsync(Push(id), Cancellation);

        Assert.Equal(PushMessageOutcome.Updated, outcome);
        _host.Presenter.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public async Task A_push_while_signed_out_is_ignored_without_calling_the_backend() {
        var id = Guid.NewGuid();

        var outcome = await Processor.HandleMessageAsync(Push(id), Cancellation);

        Assert.Equal(PushMessageOutcome.IgnoredSignedOut, outcome);
        Assert.Empty(_host.Http.Requests);
    }

    [Fact]
    public async Task A_push_that_arrives_during_logout_adds_nothing() {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        var fetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Http.On(HttpMethod.Get, $"/api/mobile/notifications/{id}", async _ => {
            fetched.SetResult();
            await release.Task;
            return StubHttpHandler.Json(HttpStatusCode.OK, BackendJson.Notification(id));
        });

        var handling = Processor.HandleMessageAsync(Push(id), Cancellation);
        await fetched.Task;
        await _host.Get<ISessionService>().LogoutAsync(Cancellation);
        release.SetResult();

        Assert.Equal(PushMessageOutcome.IgnoredSignedOut, await handling);
        Assert.Empty(Inbox.Items);
        _host.Presenter.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public async Task A_push_from_a_transport_that_is_not_active_is_ignored() {
        _host.Get<PushTransportSelector>().Mode = PushMode.InboxOnly;
        await _host.SignInAsync();

        var outcome = await Processor.HandleMessageAsync(Push(Guid.NewGuid()), Cancellation);

        Assert.Equal(PushMessageOutcome.IgnoredInactiveTransport, outcome);
    }

    [Fact]
    public async Task Tapping_a_notification_opens_its_detail_page() {
        await _host.SignInAsync();
        await _host.Get<NavigationCoordinator>().OnShellReadyAsync();
        var id = Guid.NewGuid();

        await Processor.HandleOpenedAsync(Data(id.ToString()), "intent");

        await _host.Navigation.Received(1).OpenNotificationAsync(id);
        Assert.Equal("Opened", Processor.LastEvent?.Kind);
    }

    [Fact]
    public async Task A_tap_with_a_missing_or_invalid_id_opens_the_inbox() {
        await _host.SignInAsync();
        await _host.Get<NavigationCoordinator>().OnShellReadyAsync();

        await Processor.HandleOpenedAsync(Data("not-a-guid"), "intent");

        await _host.Navigation.Received(1).OpenInboxAsync();
        await _host.Navigation.DidNotReceiveWithAnyArgs().OpenNotificationAsync(default);
    }

    [Fact]
    public async Task A_tap_that_launched_the_app_waits_for_login_and_then_opens_the_notification() {
        var id = Guid.NewGuid();
        var coordinator = _host.Get<NavigationCoordinator>();

        await Processor.HandleOpenedAsync(Data(id.ToString()), "intent");
        await _host.Navigation.DidNotReceiveWithAnyArgs().OpenNotificationAsync(default);

        await _host.SignInAsync();
        await coordinator.OnShellReadyAsync();

        await _host.Navigation.Received(1).OpenNotificationAsync(id);
        Assert.False(coordinator.HasPending);
    }

    [Fact]
    public async Task Diagnostics_record_push_metadata_but_no_content() {
        await _host.SignInAsync();
        var id = ServeNotification();

        await Processor.HandleMessageAsync(Push(id), Cancellation);

        var record = Processor.LastEvent!;
        Assert.Equal(("Received", "fcm", id.ToString(), "demo.alert", "Presented"), (record.Kind, record.Source, record.NotificationId, record.Type, record.Outcome));
    }

    private Guid ServeNotification(DateTimeOffset? readAt = null) {
        var id = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, $"/api/mobile/notifications/{id}", HttpStatusCode.OK, BackendJson.Notification(id, readAt: readAt));
        return id;
    }

    private PushMessage Push(Guid? id, bool hasDisplayedNotification = false, string? rawId = null) =>
        new("fcm", Data(rawId ?? id?.ToString()), hasDisplayedNotification, _host.Time.GetUtcNow(), "msg-1");

    private static Dictionary<string, string> Data(string? notificationId) {
        var data = new Dictionary<string, string> { [PushDataKeys.Type] = "demo.alert" };
        if (notificationId is not null) {
            data[PushDataKeys.NotificationId] = notificationId;
        }

        return data;
    }

    private static async Task WaitUntilAsync(Func<bool> condition) {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
