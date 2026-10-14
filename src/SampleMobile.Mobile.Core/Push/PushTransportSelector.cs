using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Core.Push;

/// <summary>How the user wants notifications delivered (a development switch on the login screen).</summary>
public enum PushMode {
    /// <summary>Use the real push transport when it is configured and available, otherwise inbox-only.</summary>
    Automatic,
    /// <summary>Never use real push; keep the inbox in sync through the API only.</summary>
    InboxOnly
}

/// <summary>
/// Chooses the active transport: the app's real transport (registered by the platform project) when the mode
/// allows it and it initializes, otherwise <see cref="InboxOnlyPushTransport"/>. A provider outage therefore
/// degrades to inbox-only instead of failing.
/// </summary>
public sealed class PushTransportSelector {
    internal const string ModePreferenceKey = "push.mode";

    private readonly InboxOnlyPushTransport _inboxOnly;
    private readonly IAppPreferences _preferences;
    private readonly ILogger<PushTransportSelector> _logger;
    private readonly Lock _sync = new();
    private IPushTransport _active;
    private PushTransportStatus _activeStatus = PushTransportStatus.NotInitialized;
    private PushTransportStatus _preferredStatus = PushTransportStatus.NotInitialized;

    public PushTransportSelector(IEnumerable<IPushTransport> transports, InboxOnlyPushTransport inboxOnly, IAppPreferences preferences, ILogger<PushTransportSelector> logger) {
        _inboxOnly = inboxOnly;
        _preferences = preferences;
        _logger = logger;
        Transports = transports.Append(inboxOnly).Distinct().ToList();
        Preferred = Transports.FirstOrDefault(transport => transport.DeliversRealPush);
        _active = inboxOnly;
    }

    /// <summary>Every registered transport, including the inbox-only fallback.</summary>
    public IReadOnlyList<IPushTransport> Transports { get; }

    /// <summary>The real push transport this build registers, if any.</summary>
    public IPushTransport? Preferred { get; }

    public IPushTransport Active {
        get {
            lock (_sync) {
                return _active;
            }
        }
    }

    public PushTransportStatus ActiveStatus {
        get {
            lock (_sync) {
                return _activeStatus;
            }
        }
    }

    /// <summary>Why the real transport is or isn't in use (for example "NotConfigured: google-services.json is missing").</summary>
    public PushTransportStatus PreferredStatus {
        get {
            lock (_sync) {
                return _preferredStatus;
            }
        }
    }

    public bool IsSelected => ActiveStatus.State != PushTransportState.NotInitialized;

    /// <summary>Persisted; takes effect at the next <see cref="SelectAsync"/> (the next sign-in or launch).</summary>
    public PushMode Mode {
        get => Enum.TryParse<PushMode>(_preferences.Get(ModePreferenceKey), out var mode) ? mode : PushMode.Automatic;
        set => _preferences.Set(ModePreferenceKey, value.ToString());
    }

    public event EventHandler? Changed;

    public IPushTransport? Find(string providerId) =>
        Transports.FirstOrDefault(transport => string.Equals(transport.ProviderId, providerId, StringComparison.Ordinal));

    public async Task<IPushTransport> SelectAsync(CancellationToken cancellationToken) {
        if (Preferred is not null) {
            if (Mode == PushMode.Automatic) {
                var status = await InitializeAsync(Preferred, cancellationToken);
                if (status.State == PushTransportState.Available) {
                    return Activate(Preferred, status, status);
                }

                _logger.LogWarning("{Provider} is not usable ({Status}); falling back to inbox-only", Preferred.ProviderId, status);
                return Activate(_inboxOnly, await InitializeAsync(_inboxOnly, cancellationToken), status);
            }

            var skipped = new PushTransportStatus(PushTransportState.NotInitialized, "Inbox-only mode was selected at sign-in.");
            return Activate(_inboxOnly, await InitializeAsync(_inboxOnly, cancellationToken), skipped);
        }

        var none = new PushTransportStatus(PushTransportState.NotConfigured, "This build has no push transport.");
        return Activate(_inboxOnly, await InitializeAsync(_inboxOnly, cancellationToken), none);
    }

    private async Task<PushTransportStatus> InitializeAsync(IPushTransport transport, CancellationToken cancellationToken) {
        try {
            return await transport.InitializeAsync(cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogWarning(ex, "Initializing {Provider} failed", transport.ProviderId);
            return new PushTransportStatus(PushTransportState.Unavailable, ex.Message);
        }
    }

    private IPushTransport Activate(IPushTransport transport, PushTransportStatus status, PushTransportStatus preferredStatus) {
        lock (_sync) {
            _active = transport;
            _activeStatus = status;
            _preferredStatus = preferredStatus;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return transport;
    }
}
