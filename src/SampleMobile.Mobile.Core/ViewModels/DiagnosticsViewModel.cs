using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.ViewModels;

/// <summary>
/// Development diagnostics. Shows identifiers and states, never the access token or a full provider address.
/// </summary>
public sealed partial class DiagnosticsViewModel(
    PushTransportSelector transports,
    DeviceRegistrationSync registration,
    SessionBootstrapper bootstrapper,
    InstallationIdProvider installation,
    INotificationPermissionService permissions,
    INotificationApiClient api,
    PushEventProcessor pushEvents,
    INotificationInboxService inbox,
    IBackendSettings backend,
    MobileCoreOptions options,
    IMainThreadDispatcher dispatcher,
    TimeProvider time) : ObservableObject {
    private bool _active;

    [ObservableProperty]
    public partial string BackendUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Provider { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TransportStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaskedAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AddressStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InstallationId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Platform { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PermissionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegistrationResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PendingCleanup { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ApiStatus { get; set; } = "Not checked";

    [ObservableProperty]
    public partial string LastPushEvent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InboxStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task ActivateAsync() {
        if (!_active) {
            _active = true;
            transports.Changed += OnChanged;
            registration.Changed += OnChanged;
            bootstrapper.Changed += OnChanged;
            pushEvents.Changed += OnChanged;
            inbox.Changed += OnChanged;
        }

        await UpdateAsync();
    }

    public void Deactivate() {
        if (!_active) {
            return;
        }

        _active = false;
        transports.Changed -= OnChanged;
        registration.Changed -= OnChanged;
        bootstrapper.Changed -= OnChanged;
        pushEvents.Changed -= OnChanged;
        inbox.Changed -= OnChanged;
    }

    [RelayCommand]
    private async Task RefreshRegistrationAsync() {
        IsBusy = true;
        try {
            await bootstrapper.RefreshRegistrationAsync();
        } finally {
            IsBusy = false;
            await UpdateAsync();
        }
    }

    [RelayCommand]
    private async Task CheckApiAsync() {
        IsBusy = true;
        try {
            var result = await api.GetNotificationsAsync(page: 1, pageSize: 1);
            ApiStatus = $"{result.Describe()} at {DisplayText.Time(time.GetUtcNow(), time)}";
        } finally {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncInboxAsync() {
        IsBusy = true;
        try {
            await inbox.RefreshAsync();
        } finally {
            IsBusy = false;
            await UpdateAsync();
        }
    }

    private async Task UpdateAsync() {
        BackendUrl = backend.BaseUri?.ToString() ?? "(not set)";

        var active = transports.Active;
        Provider = $"{active.ProviderId} ({active.DisplayName})";
        TransportStatus = transports.Preferred is { } preferred
            ? $"active: {transports.ActiveStatus} · {preferred.ProviderId}: {transports.PreferredStatus}"
            : $"active: {transports.ActiveStatus}";

        MaskedAddress = await registration.GetMaskedAddressAsync() ?? "(none yet)";
        AddressStatus = bootstrapper.LastAddressResult switch {
            null => "not requested yet",
            { Succeeded: true } => "obtained",
            var failed => $"{failed.Failure}: {failed.Reason}"
        };

        InstallationId = installation.GetOrCreate();
        Platform = options.Platform;
        PermissionStatus = DisplayText.Permission(await permissions.GetStatusAsync());
        RegistrationResult = DisplayText.Registration(registration.Status, registration.LastResult, time);

        var pending = await registration.GetPendingCleanupAsync();
        PendingCleanup = pending.Count == 0
            ? "none"
            : string.Join("; ", pending.Select(cleanup =>
                $"{cleanup.Provider}: {(cleanup.DeletePending ? "unregister " : string.Empty)}{(cleanup.ResetPending ? "reset " : string.Empty)}since {DisplayText.Time(cleanup.CreatedAt, time)}"));

        LastPushEvent = pushEvents.LastEvent is { } last
            ? $"{last.Kind} via {last.Source} at {DisplayText.Time(last.At, time)} · id {last.NotificationId ?? "(none)"} · type {last.Type ?? "(none)"} · {last.Outcome}"
            : "none since launch";

        InboxStatus = inbox.HasLoaded
            ? $"{inbox.Items.Count} loaded, {inbox.UnreadCount} unread, refreshed {DisplayText.Time(inbox.LastRefreshedAt, time)}"
            : inbox.LastError is { } error ? $"not loaded: {error.Describe()}" : "not loaded";
    }

    private void OnChanged(object? sender, EventArgs e) => dispatcher.Dispatch(() => _ = UpdateAsync());
}
