using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Api.Contracts;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.Notifications;

/// <summary>What happened when a push hint was applied to the inbox.</summary>
public enum PushHintOutcome {
    /// <summary>The notification was fetched from the API and is in the inbox.</summary>
    Fetched,
    /// <summary>The hint had no usable id, so the whole first page was refreshed instead.</summary>
    Refreshed,
    /// <summary>404: the notification doesn't exist or belongs to another user (for example a previous user on this device).</summary>
    NotFound,
    /// <summary>The API couldn't be reached; the hint should be retried later.</summary>
    TransientFailure,
    /// <summary>Nothing to do (signed out, or an unexpected response).</summary>
    Ignored
}

/// <summary>Result of <see cref="INotificationInboxService.ApplyPushHintAsync"/>.</summary>
public sealed record PushHintResult(PushHintOutcome Outcome, InboxNotification? Notification = null, ApiResult? Error = null);

/// <summary>
/// The app-owned inbox. Everything comes from the backend API; a push is only a hint to fetch. Items are keyed
/// by backend id, so the same notification can never appear twice.
/// </summary>
public interface INotificationInboxService {
    /// <summary>Newest first.</summary>
    IReadOnlyList<InboxNotification> Items { get; }

    /// <summary>The user's overall unread count as last reported by the server (adjusted locally after single fetches).</summary>
    int UnreadCount { get; }

    /// <summary>Total notifications on the server; may exceed <see cref="Items"/> because only the first page is loaded.</summary>
    int TotalCount { get; }

    bool HasLoaded { get; }

    bool IsRefreshing { get; }

    DateTimeOffset? LastRefreshedAt { get; }

    /// <summary>The last failed refresh, cleared by the next successful one.</summary>
    ApiResult? LastError { get; }

    event EventHandler? Changed;

    /// <summary>Reloads the first page and replaces the list. Concurrent calls share one request.</summary>
    Task<ApiResult> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches one notification from the API (authoritative) and updates the cached copy.</summary>
    Task<ApiResult<InboxNotification>> GetAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task<ApiResult<InboxNotification>> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>Applies a push hint: fetch by id when it is a valid GUID, otherwise refresh the list.</summary>
    Task<PushHintResult> ApplyPushHintAsync(string? notificationId, CancellationToken cancellationToken = default);

    /// <summary>Forgets everything (sign-out, expiry, user change).</summary>
    void Clear();
}

/// <inheritdoc />
public sealed class NotificationInboxService(INotificationApiClient api, ISessionStore sessions, TimeProvider time) : INotificationInboxService {
    internal const int PageSize = 50;

    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, InboxNotification> _items = [];
    private IReadOnlyList<InboxNotification> _snapshot = [];
    private TaskCompletionSource<ApiResult>? _refresh;
    private int _unreadCount;
    private int _totalCount;
    private bool _hasLoaded;
    private DateTimeOffset? _lastRefreshedAt;
    private ApiResult? _lastError;

    public event EventHandler? Changed;

    public IReadOnlyList<InboxNotification> Items {
        get {
            lock (_sync) {
                return _snapshot;
            }
        }
    }

    public int UnreadCount {
        get {
            lock (_sync) {
                return _unreadCount;
            }
        }
    }

    public int TotalCount {
        get {
            lock (_sync) {
                return _totalCount;
            }
        }
    }

    public bool HasLoaded {
        get {
            lock (_sync) {
                return _hasLoaded;
            }
        }
    }

    public bool IsRefreshing {
        get {
            lock (_sync) {
                return _refresh is not null;
            }
        }
    }

    public DateTimeOffset? LastRefreshedAt {
        get {
            lock (_sync) {
                return _lastRefreshedAt;
            }
        }
    }

    public ApiResult? LastError {
        get {
            lock (_sync) {
                return _lastError;
            }
        }
    }

    public async Task<ApiResult> RefreshAsync(CancellationToken cancellationToken = default) {
        var owned = new TaskCompletionSource<ApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ApiResult>? inFlight;
        lock (_sync) {
            inFlight = _refresh;
            _refresh ??= owned;
        }

        if (inFlight is not null) {
            return await inFlight.Task;
        }

        RaiseChanged();
        try {
            var result = await RefreshCoreAsync(cancellationToken);
            owned.SetResult(result);
            return result;
        } catch (OperationCanceledException) {
            owned.TrySetCanceled(cancellationToken);
            throw;
        } catch (Exception ex) {
            owned.TrySetException(ex);
            _ = owned.Task.Exception;
            throw;
        } finally {
            lock (_sync) {
                _refresh = null;
            }

            RaiseChanged();
        }
    }

    public async Task<ApiResult<InboxNotification>> GetAsync(Guid notificationId, CancellationToken cancellationToken = default) {
        var generation = sessions.Generation;
        var result = await api.GetNotificationAsync(notificationId, cancellationToken);
        return Apply(notificationId, generation, result);
    }

    public async Task<ApiResult<InboxNotification>> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default) {
        var generation = sessions.Generation;
        var result = await api.MarkAsReadAsync(notificationId, cancellationToken);
        return Apply(notificationId, generation, result);
    }

    public async Task<PushHintResult> ApplyPushHintAsync(string? notificationId, CancellationToken cancellationToken = default) {
        if (!Guid.TryParse(notificationId, out var id)) {
            var refreshed = await RefreshAsync(cancellationToken);
            return refreshed.Succeeded
                ? new PushHintResult(PushHintOutcome.Refreshed)
                : new PushHintResult(refreshed.IsTransient ? PushHintOutcome.TransientFailure : PushHintOutcome.Ignored, Error: refreshed);
        }

        var result = await GetAsync(id, cancellationToken);
        if (result.Succeeded) {
            return new PushHintResult(PushHintOutcome.Fetched, result.Value);
        }

        var outcome = result.Failure switch {
            ApiFailureKind.NotFound => PushHintOutcome.NotFound,
            _ when result.IsTransient => PushHintOutcome.TransientFailure,
            _ => PushHintOutcome.Ignored
        };
        return new PushHintResult(outcome, Error: result);
    }

    public void Clear() {
        lock (_sync) {
            _items.Clear();
            _unreadCount = 0;
            _totalCount = 0;
            _hasLoaded = false;
            _lastRefreshedAt = null;
            _lastError = null;
            Rebuild();
        }

        RaiseChanged();
    }

    private async Task<ApiResult> RefreshCoreAsync(CancellationToken cancellationToken) {
        var generation = sessions.Generation;
        var result = await api.GetNotificationsAsync(1, PageSize, unreadOnly: false, cancellationToken);

        lock (_sync) {
            if (generation != sessions.Generation) {
                // Another user (or nobody) is signed in now; this page belongs to the previous session.
                return result;
            }

            if (result.Value is { } page) {
                _items.Clear();
                foreach (var item in page.Items) {
                    _items[item.Id] = InboxNotification.From(item);
                }

                _unreadCount = page.UnreadCount;
                _totalCount = page.TotalCount;
                _hasLoaded = true;
                _lastRefreshedAt = time.GetUtcNow();
                _lastError = null;
                Rebuild();
            } else {
                _lastError = result;
            }
        }

        return result;
    }

    private ApiResult<InboxNotification> Apply(Guid notificationId, long generation, ApiResult<NotificationResponse> result) {
        if (!result.Succeeded) {
            if (result.Failure == ApiFailureKind.NotFound) {
                Remove(notificationId, generation);
            }

            return ApiResult<InboxNotification>.From(result);
        }

        var notification = InboxNotification.From(result.Value!);
        var changed = false;
        lock (_sync) {
            if (generation == sessions.Generation) {
                Upsert(notification);
                Rebuild();
                changed = true;
            }
        }

        if (changed) {
            RaiseChanged();
        }

        return ApiResult<InboxNotification>.Success(notification, result.StatusCode ?? 200);
    }

    private void Remove(Guid notificationId, long generation) {
        lock (_sync) {
            if (generation != sessions.Generation || !_items.Remove(notificationId, out var removed)) {
                return;
            }

            _totalCount = Math.Max(0, _totalCount - 1);
            if (!removed.IsRead) {
                _unreadCount = Math.Max(0, _unreadCount - 1);
            }

            Rebuild();
        }

        RaiseChanged();
    }

    // Caller holds _sync. Keeps the counts consistent until the next refresh replaces them with the server's.
    private void Upsert(InboxNotification notification) {
        if (_items.TryGetValue(notification.Id, out var existing)) {
            if (!existing.IsRead && notification.IsRead) {
                _unreadCount = Math.Max(0, _unreadCount - 1);
            } else if (existing.IsRead && !notification.IsRead) {
                _unreadCount++;
            }
        } else {
            _totalCount++;
            if (!notification.IsRead) {
                _unreadCount++;
            }
        }

        _items[notification.Id] = notification;
    }

    // Caller holds _sync.
    private void Rebuild() =>
        _snapshot = _items.Values
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToList();

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
