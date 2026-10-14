namespace SampleMobile.Mobile.Core.Push;

/// <summary>
/// A push delivery mechanism (FCM today, perhaps OneSignal later), seen through a provider-independent contract.
/// A transport only alerts the device that something changed; notification content always comes from the API.
/// Provider SDK types never cross this interface.
/// </summary>
public interface IPushTransport {
    /// <summary>The backend's provider id for registrations made with this transport (<c>fcm</c>, <c>simulated</c>…).</summary>
    string ProviderId { get; }

    /// <summary>Human-readable name for status screens.</summary>
    string DisplayName { get; }

    /// <summary>False for the inbox-only fallback, which never wakes the device.</summary>
    bool DeliversRealPush { get; }

    /// <summary>Initializes the provider SDK. Never throws for a missing configuration; reports it instead.</summary>
    Task<PushTransportStatus> InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Allows the provider to issue an address (called when a user signs in).</summary>
    Task ActivateAsync(CancellationToken cancellationToken);

    /// <summary>Returns the current registration address (token), creating one if needed.</summary>
    Task<PushAddressResult> GetRegistrationAddressAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops address issuance and invalidates the local address (sign-out), so a registration the backend still
    /// holds becomes undeliverable. Returns false when it must be retried later (for example offline).
    /// </summary>
    Task<bool> ResetRegistrationAsync(CancellationToken cancellationToken);

    /// <summary>The provider rotated the address. It must be sent to the backend again.</summary>
    event EventHandler<PushAddressChangedEventArgs>? AddressChanged;

    /// <summary>A push was handed to the running app (the OS did not display it).</summary>
    event EventHandler<PushMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// The user tapped a notification, for providers whose SDK reports taps through a callback. On Android, taps on
    /// FCM tray notifications arrive as launcher intents instead and are routed by the app's intent handler.
    /// </summary>
    event EventHandler<PushNotificationOpenedEventArgs>? NotificationOpened;
}

/// <summary>Whether a transport can be used.</summary>
public enum PushTransportState {
    NotInitialized,
    Available,
    /// <summary>The provider isn't configured in this build (for example no google-services.json).</summary>
    NotConfigured,
    /// <summary>Configured but unusable on this device right now (for example no Google Play services).</summary>
    Unavailable
}

/// <summary>A transport's state plus a human-readable reason.</summary>
public sealed record PushTransportStatus(PushTransportState State, string? Reason = null) {
    public static readonly PushTransportStatus NotInitialized = new(PushTransportState.NotInitialized);

    public static readonly PushTransportStatus Available = new(PushTransportState.Available);

    public override string ToString() => Reason is null ? State.ToString() : $"{State}: {Reason}";
}

/// <summary>Whether a failure to get an address is worth retrying.</summary>
public enum PushAddressFailure {
    None,
    Transient,
    Permanent
}

/// <summary>Result of <see cref="IPushTransport.GetRegistrationAddressAsync"/>.</summary>
public sealed record PushAddressResult(string? Address, PushAddressFailure Failure, string? Reason) {
    public bool Succeeded => Failure == PushAddressFailure.None && !string.IsNullOrEmpty(Address);

    public static PushAddressResult Success(string address) => new(address, PushAddressFailure.None, null);

    public static PushAddressResult Transient(string reason) => new(null, PushAddressFailure.Transient, reason);

    public static PushAddressResult Permanent(string reason) => new(null, PushAddressFailure.Permanent, reason);
}

/// <summary>
/// A push as the app sees it. <see cref="Data"/> holds the backend's data keys (<see cref="PushDataKeys"/>);
/// title/body text from the provider is deliberately not carried, because the UI renders API data only.
/// </summary>
public sealed record PushMessage(
    string ProviderId,
    IReadOnlyDictionary<string, string> Data,
    bool HasDisplayedNotification,
    DateTimeOffset ReceivedAt,
    string? MessageId = null) {
    public PushData Parsed => PushDataParser.Parse(Data);
}

/// <summary>Raised by <see cref="IPushTransport.AddressChanged"/>.</summary>
public sealed class PushAddressChangedEventArgs(string providerId, string address) : EventArgs {
    public string ProviderId { get; } = providerId;

    public string Address { get; } = address;
}

/// <summary>Raised by <see cref="IPushTransport.MessageReceived"/>.</summary>
public sealed class PushMessageReceivedEventArgs(PushMessage message) : EventArgs {
    public PushMessage Message { get; } = message;
}

/// <summary>Raised by <see cref="IPushTransport.NotificationOpened"/>.</summary>
public sealed class PushNotificationOpenedEventArgs(IReadOnlyDictionary<string, string> data) : EventArgs {
    public IReadOnlyDictionary<string, string> Data { get; } = data;
}
