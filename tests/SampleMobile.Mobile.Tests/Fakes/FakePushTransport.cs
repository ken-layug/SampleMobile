using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Tests.Fakes;

/// <summary>A scriptable push transport standing in for FCM. <see cref="Calls"/> records the order of operations.</summary>
public sealed class FakePushTransport(string providerId = "fcm") : IPushTransport {
    private readonly Lock _sync = new();
    private readonly List<string> _calls = [];

    public string ProviderId { get; } = providerId;

    public string DisplayName => $"Fake {ProviderId}";

    public bool DeliversRealPush => true;

    public PushTransportStatus Status { get; set; } = PushTransportStatus.Available;

    /// <summary>What the next address requests return.</summary>
    public PushAddressResult AddressResult { get; set; } = PushAddressResult.Success("fcm-token-0123456789-A");

    public bool ResetSucceeds { get; set; } = true;

    public IReadOnlyList<string> Calls {
        get {
            lock (_sync) {
                return _calls.ToList();
            }
        }
    }

    public event EventHandler<PushAddressChangedEventArgs>? AddressChanged;

    public event EventHandler<PushMessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<PushNotificationOpenedEventArgs>? NotificationOpened;

    public Task<PushTransportStatus> InitializeAsync(CancellationToken cancellationToken) {
        Log("initialize");
        return Task.FromResult(Status);
    }

    public Task ActivateAsync(CancellationToken cancellationToken) {
        Log("activate");
        return Task.CompletedTask;
    }

    public Task<PushAddressResult> GetRegistrationAddressAsync(CancellationToken cancellationToken) {
        Log("address");
        return Task.FromResult(AddressResult);
    }

    public Task<bool> ResetRegistrationAsync(CancellationToken cancellationToken) {
        Log(ResetSucceeds ? "reset" : "reset-failed");
        return Task.FromResult(ResetSucceeds);
    }

    public void RaiseAddressChanged(string address) {
        AddressResult = PushAddressResult.Success(address);
        AddressChanged?.Invoke(this, new PushAddressChangedEventArgs(ProviderId, address));
    }

    public void RaiseMessage(PushMessage message) => MessageReceived?.Invoke(this, new PushMessageReceivedEventArgs(message));

    public void RaiseOpened(IReadOnlyDictionary<string, string> data) => NotificationOpened?.Invoke(this, new PushNotificationOpenedEventArgs(data));

    private void Log(string call) {
        lock (_sync) {
            _calls.Add(call);
        }
    }
}
