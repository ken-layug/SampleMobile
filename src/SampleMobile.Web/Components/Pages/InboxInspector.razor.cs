using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Features.Notifications;
using SampleMobile.Application.Features.Users;

namespace SampleMobile.Web.Components.Pages;

public partial class InboxInspector {
    private const int PageSize = 20;

    [Inject]
    private IDashboardSender Sender { get; set; } = null!;

    [PersistentState]
    public IReadOnlyList<UserSummary>? Users { get; set; }

    private Guid? SelectedUserId { get; set; }
    private bool UnreadOnly { get; set; }
    private int CurrentPage { get; set; } = 1;
    private NotificationListResponse? Inbox { get; set; }

    private int TotalPages => Inbox is null ? 0 : (int)Math.Ceiling(Inbox.TotalCount / (double)PageSize);

    protected override async Task OnInitializedAsync() {
        Users ??= (await Sender.SendAsync(new ListDemoUsersQuery())).Value ?? [];
    }

    private Task SelectUserAsync() {
        CurrentPage = 1;
        return LoadInboxAsync();
    }

    private Task ToggleUnreadOnlyAsync(ChangeEventArgs args) {
        UnreadOnly = args.Value is true;
        CurrentPage = 1;
        return LoadInboxAsync();
    }

    private Task GoToPageAsync(int page) {
        CurrentPage = page;
        return LoadInboxAsync();
    }

    private async Task LoadInboxAsync() {
        if (SelectedUserId is not { } userId) {
            Inbox = null;
            return;
        }

        var result = await Sender.SendAsync(new GetInboxQuery(userId, CurrentPage, PageSize, UnreadOnly));
        Inbox = result.Value ?? Inbox;
    }
}
