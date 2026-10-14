using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Core.ViewModels;

/// <summary>One row of the inbox list.</summary>
public sealed class NotificationItemViewModel(InboxNotification notification, string createdAt) {
    public Guid Id { get; } = notification.Id;

    public string Type { get; } = notification.Type;

    public string Title { get; } = notification.Title;

    public string Preview { get; } = DisplayText.Preview(notification.Body);

    public string CreatedAt { get; } = createdAt;

    public bool IsUnread { get; } = !notification.IsRead;

    internal InboxNotification Source { get; } = notification;
}

/// <summary>The inbox list: loading, empty and error states, pull-to-refresh, tap to open.</summary>
public sealed partial class InboxViewModel(
    INotificationInboxService inbox,
    INavigationService navigation,
    IMainThreadDispatcher dispatcher,
    TimeProvider time) : ObservableObject {
    private bool _active;

    public ObservableCollection<NotificationItemViewModel> Items { get; } = [];

    /// <summary>Bound two-way to the pull-to-refresh control.</summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    /// <summary>First load, before anything can be shown.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public void Activate() {
        if (!_active) {
            _active = true;
            inbox.Changed += OnInboxChanged;
        }

        Update();
        if (!inbox.HasLoaded && !inbox.IsRefreshing) {
            IsLoading = true;
            _ = RefreshAsync();
        }
    }

    public void Deactivate() {
        if (_active) {
            _active = false;
            inbox.Changed -= OnInboxChanged;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() {
        try {
            await inbox.RefreshAsync();
        } finally {
            IsRefreshing = false;
            IsLoading = false;
            Update();
        }
    }

    [RelayCommand]
    private Task OpenAsync(NotificationItemViewModel? item) =>
        item is null ? Task.CompletedTask : navigation.OpenNotificationAsync(item.Id);

    private void OnInboxChanged(object? sender, EventArgs e) => dispatcher.Dispatch(Update);

    private void Update() {
        var items = inbox.Items;
        if (!items.SequenceEqual(Items.Select(item => item.Source))) {
            Items.Clear();
            foreach (var item in items) {
                Items.Add(new NotificationItemViewModel(item, DisplayText.Time(item.CreatedAt, time)));
            }
        }

        var error = inbox.LastError;
        ErrorMessage = error is null ? null : error.Failure switch {
            ApiFailureKind.Network => "Can't reach the backend. Showing what was loaded last; pull down to retry.",
            ApiFailureKind.Server => $"The backend had a problem ({error.StatusCode}). Pull down to retry.",
            _ => $"Couldn't refresh: {error.Describe()}"
        };

        IsEmpty = inbox.HasLoaded && items.Count == 0;
        Summary = inbox.HasLoaded
            ? $"{inbox.UnreadCount} unread · showing {items.Count} of {inbox.TotalCount} · updated {DisplayText.Time(inbox.LastRefreshedAt, time)}"
            : string.Empty;
    }
}
