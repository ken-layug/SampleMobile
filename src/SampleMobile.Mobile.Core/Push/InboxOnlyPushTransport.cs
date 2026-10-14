namespace SampleMobile.Mobile.Core.Push;

/// <summary>
/// The no-push fallback: registers as the backend's <c>simulated</c> provider so the device still shows up in
/// the dashboard and delivery history, but nothing ever reaches the device. The inbox works through API
/// synchronization alone (app start, resume, pull-to-refresh). This is not background push.
/// </summary>
public sealed class InboxOnlyPushTransport(InstallationIdProvider installation) : IPushTransport {
    public const string Id = "simulated";

    public string ProviderId => Id;

    public string DisplayName => "Inbox-only (no real push)";

    public bool DeliversRealPush => false;

    // Never raised: this transport has no delivery channel.
    public event EventHandler<PushAddressChangedEventArgs>? AddressChanged { add { } remove { } }

    public event EventHandler<PushMessageReceivedEventArgs>? MessageReceived { add { } remove { } }

    public event EventHandler<PushNotificationOpenedEventArgs>? NotificationOpened { add { } remove { } }

    public Task<PushTransportStatus> InitializeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PushTransportStatus.Available);

    public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Not a secret: the simulated provider accepts any string and never sends anything to it.
    public Task<PushAddressResult> GetRegistrationAddressAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PushAddressResult.Success($"inbox-only:{installation.GetOrCreate()}"));

    public Task<bool> ResetRegistrationAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
