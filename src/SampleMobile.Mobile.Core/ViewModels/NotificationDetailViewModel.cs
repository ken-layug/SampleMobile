using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Notifications;

namespace SampleMobile.Mobile.Core.ViewModels;

/// <summary>
/// One notification, always fetched from the API (the push payload is never used as content). Opening it marks
/// it read, as the contract asks; the explicit action covers the case where that call failed.
/// </summary>
public sealed partial class NotificationDetailViewModel(INotificationInboxService inbox, TimeProvider time) : ObservableObject {
    private Guid? _notificationId;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CreatedAt { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReadState { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Details { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MarkAsReadCommand))]
    public partial bool IsRead { get; set; }

    [ObservableProperty]
    public partial bool HasContent { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MarkAsReadCommand))]
    public partial bool IsLoading { get; set; }

    /// <summary>The notification doesn't exist, isn't this user's, or the link was invalid.</summary>
    [ObservableProperty]
    public partial bool IsUnavailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Takes the <c>id</c> navigation parameter. Anything that isn't a GUID shows "not available".</summary>
    public void SetNotificationId(string? value) {
        _notificationId = Guid.TryParse(value, out var id) ? id : null;
        HasContent = false;
        IsUnavailable = _notificationId is null;
        ErrorMessage = null;
    }

    [RelayCommand]
    public async Task LoadAsync() {
        if (_notificationId is not { } id) {
            IsUnavailable = true;
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try {
            var result = await inbox.GetAsync(id);
            if (!Apply(result)) {
                return;
            }

            if (!IsRead) {
                Apply(await inbox.MarkAsReadAsync(id));
            }
        } finally {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanMarkAsRead))]
    private async Task MarkAsReadAsync() {
        if (_notificationId is not { } id) {
            return;
        }

        IsLoading = true;
        try {
            Apply(await inbox.MarkAsReadAsync(id));
        } finally {
            IsLoading = false;
        }
    }

    private bool CanMarkAsRead() => HasContent && !IsRead && !IsLoading;

    private bool Apply(ApiResult<InboxNotification> result) {
        if (result.Value is { } notification) {
            Title = notification.Title;
            Body = notification.Body;
            Type = notification.Type;
            CreatedAt = DisplayText.Time(notification.CreatedAt, time);
            IsRead = notification.IsRead;
            ReadState = notification.ReadAt is { } readAt ? $"Read {DisplayText.Time(readAt, time)}" : "Unread";
            Details = string.Join(Environment.NewLine, notification.Metadata
                .Select(pair => $"{pair.Key}: {pair.Value}")
                .Prepend(notification.ResourceId is null ? string.Empty : $"resource: {notification.ResourceId}")
                .Where(line => line.Length > 0));
            HasContent = true;
            IsUnavailable = false;
            MarkAsReadCommand.NotifyCanExecuteChanged();
            return true;
        }

        if (result.Failure == ApiFailureKind.NotFound) {
            HasContent = false;
            IsUnavailable = true;
            return false;
        }

        ErrorMessage = result.Failure == ApiFailureKind.Network
            ? "Can't reach the backend. Try again when you're online."
            : result.Describe();
        return false;
    }
}
