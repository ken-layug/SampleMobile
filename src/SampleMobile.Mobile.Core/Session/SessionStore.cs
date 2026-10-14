using System.Text.Json;
using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Core.Session;

/// <summary>
/// Holds the current session and persists it in secure storage. It has no API dependency, so the HTTP
/// pipeline can read the token from it without a dependency cycle.
/// </summary>
public interface ISessionStore {
    /// <summary>The loaded, unexpired session, or null. Call <see cref="GetActiveAsync"/> first after a cold start.</summary>
    UserSession? Current { get; }

    /// <summary>Incremented on every sign-in, sign-out and expiry. Work that captured an older value must drop its result.</summary>
    long Generation { get; }

    /// <summary>Cancelled when the session that was current when it was read ends.</summary>
    CancellationToken SessionToken { get; }

    /// <summary>Loads the session from secure storage on first use (also in a process started by a push service without UI).</summary>
    Task<UserSession?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task SignInAsync(UserSession session);

    /// <summary>Ends the session. Returns the session that ended, or null if nobody was signed in.</summary>
    Task<UserSession?> SignOutAsync();

    /// <summary>Expires the session only if it still uses <paramref name="accessToken"/>, so a late 401 from an old token is harmless.</summary>
    Task<bool> ExpireIfCurrentAsync(string accessToken);

    event EventHandler<SessionChangedEventArgs>? Changed;
}

/// <inheritdoc />
public sealed class SessionStore(ISecureStore secureStore, TimeProvider time, ILogger<SessionStore> logger) : ISessionStore {
    internal const string StorageKey = "session";

    // Treat the token as expired slightly early so a request doesn't race the server's clock skew.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private bool _loaded;
    private UserSession? _session;
    private long _generation;
    private CancellationTokenSource _sessionCancellation = new();

    public event EventHandler<SessionChangedEventArgs>? Changed;

    public UserSession? Current {
        get {
            lock (_sync) {
                return _session is not null && !IsExpired(_session) ? _session : null;
            }
        }
    }

    public long Generation {
        get {
            lock (_sync) {
                return _generation;
            }
        }
    }

    public CancellationToken SessionToken {
        get {
            lock (_sync) {
                return _sessionCancellation.Token;
            }
        }
    }

    public async Task<UserSession?> GetActiveAsync(CancellationToken cancellationToken = default) {
        if (!Volatile.Read(ref _loaded)) {
            await _gate.WaitAsync(cancellationToken);
            try {
                await LoadAsync();
            } finally {
                _gate.Release();
            }
        }

        UserSession? expired;
        lock (_sync) {
            if (_session is null) {
                return null;
            }

            if (!IsExpired(_session)) {
                return _session;
            }

            expired = _session;
        }

        await ExpireIfCurrentAsync(expired.AccessToken);
        return null;
    }

    public async Task SignInAsync(UserSession session) {
        CancellationTokenSource previous;
        await _gate.WaitAsync();
        try {
            await secureStore.SetAsync(StorageKey, JsonSerializer.Serialize(session, MobileStateJsonContext.Default.UserSession));
            lock (_sync) {
                _session = session;
                _loaded = true;
                previous = Advance();
            }
        } finally {
            _gate.Release();
        }

        previous.Cancel();
        Changed?.Invoke(this, new SessionChangedEventArgs(SessionChangeKind.SignedIn, session.UserName));
    }

    public Task<UserSession?> SignOutAsync() => EndAsync(SessionChangeKind.SignedOut, expectedToken: null);

    public async Task<bool> ExpireIfCurrentAsync(string accessToken) =>
        await EndAsync(SessionChangeKind.Expired, accessToken) is not null;

    private async Task<UserSession?> EndAsync(SessionChangeKind kind, string? expectedToken) {
        UserSession ended;
        CancellationTokenSource previous;
        await _gate.WaitAsync();
        try {
            await LoadAsync();
            lock (_sync) {
                if (_session is null || (expectedToken is not null && _session.AccessToken != expectedToken)) {
                    return null;
                }

                ended = _session;
                _session = null;
                previous = Advance();
            }

            secureStore.Remove(StorageKey);
        } finally {
            _gate.Release();
        }

        previous.Cancel();
        logger.LogInformation("Session for {UserName} ended: {Kind}", ended.UserName, kind);
        Changed?.Invoke(this, new SessionChangedEventArgs(kind, ended.UserName));
        return ended;
    }

    // Caller holds _gate.
    private async Task LoadAsync() {
        if (_loaded) {
            return;
        }

        UserSession? stored = null;
        try {
            var json = await secureStore.GetAsync(StorageKey);
            stored = json is null ? null : JsonSerializer.Deserialize(json, MobileStateJsonContext.Default.UserSession);
        } catch (Exception ex) when (ex is JsonException or NotSupportedException) {
            logger.LogWarning(ex, "Discarding an unreadable stored session");
            secureStore.Remove(StorageKey);
        }

        lock (_sync) {
            _session = stored;
            _loaded = true;
        }
    }

    // Caller holds _sync. Returns the token source to cancel once the lock is released.
    private CancellationTokenSource Advance() {
        _generation++;
        var previous = _sessionCancellation;
        _sessionCancellation = new CancellationTokenSource();
        return previous;
    }

    private bool IsExpired(UserSession session) => session.ExpiresAt - ExpirySkew <= time.GetUtcNow();
}
