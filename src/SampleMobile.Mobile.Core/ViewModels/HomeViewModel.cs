using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.ViewModels;

/// <summary>Home: who is signed in, which push transport is active, permission, registration and inbox status.</summary>
public sealed partial class HomeViewModel(
    ISessionStore sessions,
    ISessionService sessionService,
    PushTransportSelector transports,
    DeviceRegistrationSync registration,
    INotificationPermissionService permissions,
    INotificationInboxService inbox,
    INavigationService navigation,
    IMainThreadDispatcher dispatcher,
    TimeProvider time) : ObservableObject {
    private bool _active;

    [ObservableProperty]
    public partial string SignedInAs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SessionExpires { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProviderName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProviderId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TransportStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInboxOnly { get; set; }

    [ObservableProperty]
    public partial string PermissionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPermissionDenied { get; set; }

    [ObservableProperty]
    public partial string RegistrationStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int UnreadCount { get; set; }

    [ObservableProperty]
    public partial string LastInboxRefresh { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Called when the page appears: start listening and show current state.</summary>
    public async Task ActivateAsync() {
        if (!_active) {
            _active = true;
            transports.Changed += OnStateChanged;
            registration.Changed += OnStateChanged;
            inbox.Changed += OnStateChanged;
            permissions.Changed += OnPermissionChanged;
        }

        Update();
        await UpdatePermissionAsync();
    }

    public void Deactivate() {
        if (!_active) {
            return;
        }

        _active = false;
        transports.Changed -= OnStateChanged;
        registration.Changed -= OnStateChanged;
        inbox.Changed -= OnStateChanged;
        permissions.Changed -= OnPermissionChanged;
    }

    [RelayCommand]
    private async Task RequestPermissionAsync() {
        var state = await permissions.RequestIfNeededAsync();
        if (state == NotificationPermissionState.Denied) {
            // Android won't prompt again after a denial; the user has to allow it in settings.
            permissions.OpenSettings();
        }

        await UpdatePermissionAsync();
    }

    [RelayCommand]
    private Task OpenInboxAsync() => navigation.OpenInboxAsync();

    [RelayCommand(CanExecute = nameof(CanLogout))]
    private async Task LogoutAsync() {
        IsBusy = true;
        try {
            await sessionService.LogoutAsync();
        } finally {
            IsBusy = false;
        }
    }

    private bool CanLogout() => !IsBusy;

    private void Update() {
        var session = sessions.Current;
        SignedInAs = session?.UserName ?? "Not signed in";
        SessionExpires = session is null ? string.Empty : DisplayText.Time(session.ExpiresAt, time);

        var active = transports.Active;
        ProviderName = active.DisplayName;
        ProviderId = active.ProviderId;
        IsInboxOnly = !active.DeliversRealPush;
        TransportStatus = transports.Preferred is { } preferred && preferred != active
            ? $"{preferred.DisplayName}: {transports.PreferredStatus}"
            : transports.ActiveStatus.ToString();

        RegistrationStatus = DisplayText.Registration(registration.Status, registration.LastResult, time);
        UnreadCount = inbox.UnreadCount;
        LastInboxRefresh = DisplayText.Time(inbox.LastRefreshedAt, time);
    }

    private async Task UpdatePermissionAsync() {
        var state = await permissions.GetStatusAsync();
        PermissionStatus = DisplayText.Permission(state);
        IsPermissionDenied = state != NotificationPermissionState.Granted;
    }

    private void OnStateChanged(object? sender, EventArgs e) => dispatcher.Dispatch(Update);

    private void OnPermissionChanged(object? sender, EventArgs e) => dispatcher.Dispatch(() => _ = UpdatePermissionAsync());
}
