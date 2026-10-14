using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Navigation;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.Push;

/// <summary>What the app did with a received push.</summary>
public enum PushMessageOutcome {
    /// <summary>Fetched from the API and shown as a local notification.</summary>
    Presented,
    /// <summary>Fetched and merged into the inbox; not shown (already displayed by the OS, or already read).</summary>
    Updated,
    /// <summary>The API was unreachable; a content-free notification was shown and the inbox will catch up later.</summary>
    PresentedGeneric,
    /// <summary>No usable notification id; the inbox list was refreshed instead.</summary>
    Refreshed,
    /// <summary>404: not this user's notification (for example sent to a previous user of this device). Ignored.</summary>
    NotFound,
    /// <summary>Already handled (pushes are at-least-once).</summary>
    Duplicate,
    IgnoredSignedOut,
    IgnoredInactiveTransport,
    Failed
}

/// <summary>Metadata of the last push event, for the diagnostics screen. Contains no notification text.</summary>
public sealed record PushEventRecord(
    DateTimeOffset At,
    string Kind,
    string Source,
    string? NotificationId,
    string? Type,
    string Outcome,
    string? MessageId);

/// <summary>
/// Routes transport events into the app: address changes to registration sync, pushes to the inbox (fetching the
/// authoritative notification by id), taps to navigation. Handlers never throw back into the provider SDK.
/// </summary>
public sealed class PushEventProcessor(
    PushTransportSelector selector,
    ISessionStore sessions,
    INotificationInboxService inbox,
    ILocalNotificationPresenter presenter,
    DeviceRegistrationSync registration,
    NavigationCoordinator navigation,
    TimeProvider time,
    ILogger<PushEventProcessor> logger) {
    private const int RecentCapacity = 200;

    private readonly Lock _sync = new();
    private readonly HashSet<Guid> _recent = [];
    private readonly Queue<Guid> _recentOrder = new();
    private readonly HashSet<Guid> _inFlight = [];
    private bool _started;
    private PushEventRecord? _lastEvent;

    public event EventHandler? Changed;

    public PushEventRecord? LastEvent {
        get {
            lock (_sync) {
                return _lastEvent;
            }
        }
    }

    /// <summary>Subscribes to every transport. Called once, as early as possible in the process.</summary>
    public void Start() {
        lock (_sync) {
            if (_started) {
                return;
            }

            _started = true;
        }

        foreach (var transport in selector.Transports) {
            transport.AddressChanged += (_, e) => Run(() => registration.OnAddressChangedAsync(e.ProviderId, e.Address), "address change");
            transport.MessageReceived += (_, e) => Run(() => HandleMessageAsync(e.Message), "push message");
            transport.NotificationOpened += (_, e) => Run(() => HandleOpenedAsync(e.Data, transport.ProviderId), "notification tap");
        }
    }

    /// <summary>Handles a push delivered to the running app: fetch by id, update the inbox, show it once.</summary>
    public async Task<PushMessageOutcome> HandleMessageAsync(PushMessage message, CancellationToken cancellationToken = default) {
        var data = message.Parsed;
        var outcome = await HandleMessageCoreAsync(message, data, cancellationToken);
        Record(new PushEventRecord(time.GetUtcNow(), "Received", message.ProviderId, data.RawNotificationId, data.Type, outcome.ToString(), message.MessageId));
        return outcome;
    }

    /// <summary>Handles a notification tap from any source (provider callback or launch intent).</summary>
    public async Task HandleOpenedAsync(IReadOnlyDictionary<string, string> data, string source) {
        var parsed = PushDataParser.Parse(data);
        var outcome = parsed.NotificationId is null ? "Open inbox (no valid notification id)" : "Open notification";
        Record(new PushEventRecord(time.GetUtcNow(), "Opened", source, parsed.RawNotificationId, parsed.Type, outcome, null));

        if (parsed.NotificationId is { } id) {
            await navigation.RequestNotificationAsync(id);
        } else {
            await navigation.RequestInboxAsync();
        }
    }

    /// <summary>Forgets handled ids (the session ended).</summary>
    public void Reset() {
        lock (_sync) {
            _recent.Clear();
            _recentOrder.Clear();
        }
    }

    private async Task<PushMessageOutcome> HandleMessageCoreAsync(PushMessage message, PushData data, CancellationToken cancellationToken) {
        if (await sessions.GetActiveAsync(cancellationToken) is null) {
            return PushMessageOutcome.IgnoredSignedOut;
        }

        if (!selector.IsSelected || selector.Active.ProviderId != message.ProviderId) {
            return PushMessageOutcome.IgnoredInactiveTransport;
        }

        var generation = sessions.Generation;
        if (data.NotificationId is { } knownId && !TryBegin(knownId)) {
            return PushMessageOutcome.Duplicate;
        }

        try {
            return await ApplyAsync(message, data, generation, cancellationToken);
        } finally {
            if (data.NotificationId is { } id) {
                End(id);
            }
        }
    }

    private async Task<PushMessageOutcome> ApplyAsync(PushMessage message, PushData data, long generation, CancellationToken cancellationToken) {
        var hint = await inbox.ApplyPushHintAsync(data.NotificationId?.ToString("D") ?? data.RawNotificationId, cancellationToken);
        if (generation != sessions.Generation) {
            // Signed out while fetching: nothing of the previous session may be shown.
            return PushMessageOutcome.IgnoredSignedOut;
        }

        switch (hint.Outcome) {
            case PushHintOutcome.Fetched when hint.Notification is { } notification:
                Remember(notification.Id);
                if (message.HasDisplayedNotification || notification.IsRead) {
                    return PushMessageOutcome.Updated;
                }

                presenter.Show(notification);
                return PushMessageOutcome.Presented;
            case PushHintOutcome.NotFound:
                Remember(data.NotificationId!.Value);
                logger.LogInformation("Push for notification {NotificationId} is not visible to this user; ignored", data.NotificationId);
                return PushMessageOutcome.NotFound;
            case PushHintOutcome.TransientFailure when data.NotificationId is { } id && !message.HasDisplayedNotification:
                // Not remembered: a redelivery should fetch again and replace this entry with the real one.
                presenter.ShowGeneric(id);
                return PushMessageOutcome.PresentedGeneric;
            case PushHintOutcome.Refreshed:
                return PushMessageOutcome.Refreshed;
            default:
                return PushMessageOutcome.Failed;
        }
    }

    // False when the id was handled recently or is being handled right now.
    private bool TryBegin(Guid id) {
        lock (_sync) {
            return !_recent.Contains(id) && _inFlight.Add(id);
        }
    }

    private void End(Guid id) {
        lock (_sync) {
            _inFlight.Remove(id);
        }
    }

    private void Remember(Guid id) {
        lock (_sync) {
            if (!_recent.Add(id)) {
                return;
            }

            _recentOrder.Enqueue(id);
            while (_recentOrder.Count > RecentCapacity) {
                _recent.Remove(_recentOrder.Dequeue());
            }
        }
    }

    private void Record(PushEventRecord record) {
        lock (_sync) {
            _lastEvent = record;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Run(Func<Task> work, string what) => _ = RunSafelyAsync(work, what);

    private async Task RunSafelyAsync(Func<Task> work, string what) {
        try {
            await work();
        } catch (OperationCanceledException) {
            // The session ended while handling it.
        } catch (Exception ex) {
            logger.LogError(ex, "Handling a {What} failed", what);
        }
    }
}
