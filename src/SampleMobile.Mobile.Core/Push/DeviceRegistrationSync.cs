using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Api.Contracts;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.Push;

/// <summary>Why a registration sync was requested.</summary>
public enum RegistrationSyncReason {
    Login,
    AppStart,
    AddressChanged,
    Resume,
    Connectivity,
    Manual,
    Retry
}

/// <summary>Result of one <see cref="DeviceRegistrationSync.SyncAsync"/> call.</summary>
public enum RegistrationSyncOutcome {
    Registered,
    /// <summary>The backend already has this exact registration; nothing was sent.</summary>
    Unchanged,
    /// <summary>Not applicable right now (signed out, no transport selected yet, no address).</summary>
    Skipped,
    TransientFailure,
    PermanentFailure,
    SessionExpired
}

/// <summary>Registration state shown on the home and diagnostics screens.</summary>
public enum RegistrationStatus {
    NotRegistered,
    Syncing,
    Registered,
    PendingRetry,
    Failed
}

/// <summary>
/// Keeps the backend's device registration in step with the provider address and the signed-in user.
/// One lock serializes registration, retries and logout cleanup, so a POST can never land after the DELETE
/// of a sign-out. The latest address is persisted before any network call, so a temporary outage never loses it.
/// </summary>
public sealed class DeviceRegistrationSync(
    IDeviceRegistrationApiClient api,
    ISessionStore sessions,
    PushTransportSelector selector,
    InstallationIdProvider installation,
    RegistrationStateStore state,
    MobileCoreOptions options,
    TimeProvider time,
    ILogger<DeviceRegistrationSync> logger) {
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Lock _sync = new();
    private CancellationTokenSource? _retryCancellation;
    private Task _retryTask = Task.CompletedTask;
    private int _retryAttempt;
    private bool _appStartSynced;
    private RegistrationStatus _status = RegistrationStatus.NotRegistered;

    public event EventHandler? Changed;

    public RegistrationStatus Status {
        get {
            lock (_sync) {
                return _status;
            }
        }
    }

    public RegistrationResult? LastResult => state.LastResult;

    public bool HasPendingRetry => state.Dirty;

    /// <summary>The persisted address for the active transport, masked.</summary>
    public async Task<string?> GetMaskedAddressAsync() {
        var address = await state.GetAddressAsync(selector.Active.ProviderId);
        return address is null ? null : TokenMasker.Mask(address);
    }

    public Task<IReadOnlyList<PendingCleanup>> GetPendingCleanupAsync() => state.GetPendingCleanupAsync();

    /// <summary>Persists an address obtained from the transport, without sending it.</summary>
    public async Task StoreAddressAsync(string providerId, string address, CancellationToken cancellationToken = default) {
        await _lock.WaitAsync(cancellationToken);
        try {
            await state.SetAddressAsync(providerId, address);
        } finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// The provider rotated the address. It is persisted first, then sent if a user is signed in and the
    /// provider is the active transport. This also runs in a process a push service started without UI.
    /// </summary>
    public async Task OnAddressChangedAsync(string providerId, string address, CancellationToken cancellationToken = default) {
        await StoreAddressAsync(providerId, address, cancellationToken);
        logger.LogInformation("{Provider} issued a new address {Address}", providerId, TokenMasker.Mask(address));

        if (await sessions.GetActiveAsync(cancellationToken) is null) {
            return;
        }

        if (!selector.IsSelected) {
            await selector.SelectAsync(cancellationToken);
        }

        if (selector.Active.ProviderId == providerId) {
            await SyncAsync(RegistrationSyncReason.AddressChanged, cancellationToken);
        }
    }

    /// <summary>
    /// Sends the latest persisted address when it (or the user) changed since the last accepted registration,
    /// or always for login, the first sync after launch and manual refreshes. Otherwise does nothing.
    /// </summary>
    public async Task<RegistrationSyncOutcome> SyncAsync(RegistrationSyncReason reason, CancellationToken cancellationToken = default) {
        await _lock.WaitAsync(cancellationToken);
        try {
            return await SyncLockedAsync(reason, cancellationToken);
        } finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// First step of a sign-out, while the session is still current: records the cleanup (unregister with this
    /// user's token, then reset the provider address) durably, before the token is discarded. It waits for any
    /// in-flight registration, and cancels scheduled retries. <see cref="ProcessPendingCleanupAsync"/> performs it.
    /// </summary>
    public async Task BeginLogoutAsync(UserSession session, CancellationToken cancellationToken = default) {
        CancelRetry();
        await _lock.WaitAsync(cancellationToken);
        try {
            var provider = state.RegisteredProvider ?? selector.Active.ProviderId;
            var cleanup = new PendingCleanup(
                installation.GetOrCreate(),
                provider,
                session.AccessToken,
                session.ExpiresAt,
                DeletePending: true,
                ResetPending: true,
                time.GetUtcNow());

            var pending = (await state.GetPendingCleanupAsync())
                .Where(existing => existing.InstallationId != cleanup.InstallationId || existing.Provider != cleanup.Provider)
                .Append(cleanup)
                .ToList();
            await state.SetPendingCleanupAsync(pending);

            state.Fingerprint = null;
            state.RegisteredProvider = null;
            state.Dirty = false;
            _appStartSynced = false;
            _retryAttempt = 0;
            state.LastResult = new RegistrationResult(time.GetUtcNow(), provider, true, null, "Unregistering (signed out)", null);
            SetStatus(RegistrationStatus.NotRegistered);
        } finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// Retries leftover sign-out cleanup. While signed in, the provider reset is skipped for the active provider
    /// unless <paramref name="beforeRegistration"/> is set (it would delete the address the current user needs).
    /// </summary>
    public async Task ProcessPendingCleanupAsync(bool beforeRegistration, CancellationToken cancellationToken = default) {
        await _lock.WaitAsync(cancellationToken);
        try {
            var pending = await state.GetPendingCleanupAsync();
            if (pending.Count == 0) {
                return;
            }

            var signedIn = await sessions.GetActiveAsync(cancellationToken) is not null;
            var remaining = await RunCleanupAsync(
                pending.ToList(),
                allowResetFor: cleanup => !signedIn || beforeRegistration || (selector.IsSelected && cleanup.Provider != selector.Active.ProviderId),
                cancellationToken);
            await state.SetPendingCleanupAsync(remaining);

            if (!signedIn) {
                var done = remaining.Count == 0;
                var provider = pending[^1].Provider;
                state.LastResult = new RegistrationResult(time.GetUtcNow(), provider, done, null,
                    done ? "Unregistered" : "Unregister pending (will retry when online)", null);
                SetStatus(RegistrationStatus.NotRegistered);
            }
        } finally {
            _lock.Release();
        }
    }

    /// <summary>The session ended without a logout (expiry): stop retrying; the next login re-registers.</summary>
    public void OnSessionEnded() {
        CancelRetry();
        _appStartSynced = false;
        _retryAttempt = 0;
        SetStatus(RegistrationStatus.NotRegistered);
    }

    /// <summary>Completes when the currently scheduled retry (if any) has run. For tests.</summary>
    internal Task WhenIdleAsync() {
        lock (_sync) {
            return _retryTask;
        }
    }

    private async Task<RegistrationSyncOutcome> SyncLockedAsync(RegistrationSyncReason reason, CancellationToken cancellationToken) {
        var session = await sessions.GetActiveAsync(cancellationToken);
        if (session is null || !selector.IsSelected) {
            return RegistrationSyncOutcome.Skipped;
        }

        var generation = sessions.Generation;
        var provider = selector.Active.ProviderId;
        var address = await state.GetAddressAsync(provider);
        if (string.IsNullOrEmpty(address)) {
            return RegistrationSyncOutcome.Skipped;
        }

        var installationId = installation.GetOrCreate();
        var fingerprint = Fingerprint(session.UserName, provider, installationId, address);
        var force = reason is RegistrationSyncReason.Login or RegistrationSyncReason.Manual
            || (reason == RegistrationSyncReason.AppStart && !_appStartSynced);
        if (!force && !state.Dirty && fingerprint == state.Fingerprint) {
            return RegistrationSyncOutcome.Unchanged;
        }

        SetStatus(RegistrationStatus.Syncing);
        var request = new RegisterDeviceRequest(installationId, options.Platform, provider, address);
        var result = await api.RegisterAsync(request, cancellationToken);
        if (result.Failure == ApiFailureKind.Conflict) {
            // The contract: two registrations for the same installation raced. Retry once.
            result = await api.RegisterAsync(request, cancellationToken);
        }

        if (generation != sessions.Generation) {
            // Signed out meanwhile; the sign-out's DELETE runs after this under the same lock.
            return RegistrationSyncOutcome.Skipped;
        }

        var masked = TokenMasker.Mask(address);
        if (result.Value is { } registration) {
            state.Fingerprint = fingerprint;
            state.RegisteredProvider = provider;
            state.Dirty = false;
            _appStartSynced = true;
            _retryAttempt = 0;
            await SupersedePendingCleanupAsync(installationId, provider);

            var summary = result.StatusCode == 201 ? "Registered (created)" : "Registered (refreshed)";
            if (!registration.Enabled) {
                summary += $", but disabled: {registration.DisabledReason}";
            }

            Record(provider, result, summary, masked);
            SetStatus(RegistrationStatus.Registered);
            logger.LogInformation("Registered {Provider} address {Address} ({Reason})", provider, masked, reason);
            return RegistrationSyncOutcome.Registered;
        }

        Record(provider, result, result.Describe(), masked);
        if (result.Failure == ApiFailureKind.Unauthorized) {
            // BearerTokenHandler already expired the session; the next login registers again.
            SetStatus(RegistrationStatus.Failed);
            return RegistrationSyncOutcome.SessionExpired;
        }

        if (result.IsTransient) {
            state.Dirty = true;
            SetStatus(RegistrationStatus.PendingRetry);
            ScheduleRetry();
            return RegistrationSyncOutcome.TransientFailure;
        }

        state.Dirty = false;
        SetStatus(RegistrationStatus.Failed);
        logger.LogWarning("Registration of {Provider} was rejected: {Result}", provider, result.Describe());
        return RegistrationSyncOutcome.PermanentFailure;
    }

    private async Task<List<PendingCleanup>> RunCleanupAsync(List<PendingCleanup> pending, Func<PendingCleanup, bool> allowResetFor, CancellationToken cancellationToken) {
        var remaining = new List<PendingCleanup>();
        foreach (var item in pending) {
            var cleanup = item;
            if (cleanup.DeletePending) {
                cleanup = await DeleteAsync(cleanup, cancellationToken);
            }

            if (cleanup.ResetPending && allowResetFor(cleanup)) {
                cleanup = await ResetAsync(cleanup, cancellationToken);
            }

            if (!cleanup.IsComplete) {
                remaining.Add(cleanup);
            }
        }

        return remaining;
    }

    private async Task<PendingCleanup> DeleteAsync(PendingCleanup cleanup, CancellationToken cancellationToken) {
        if (cleanup.AccessToken is null || cleanup.TokenExpiresAt <= time.GetUtcNow()) {
            // The token can no longer authenticate the DELETE; the provider reset is the revocation that remains.
            return cleanup with { DeletePending = false, AccessToken = null };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CleanupTimeout);
        try {
            var result = await api.UnregisterAsync(cleanup.InstallationId, cleanup.Provider, cleanup.AccessToken, timeout.Token);
            if (result.IsTransient) {
                return cleanup;
            }

            // 204, or 401/404 (token dead, or the installation already moved to another user): nothing left to do.
            logger.LogInformation("Unregister {Provider}: {Result}", cleanup.Provider, result.Describe());
            return cleanup with { DeletePending = false, AccessToken = null };
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return cleanup;
        }
    }

    private async Task<PendingCleanup> ResetAsync(PendingCleanup cleanup, CancellationToken cancellationToken) {
        var transport = selector.Find(cleanup.Provider);
        if (transport is null) {
            return cleanup with { ResetPending = false };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CleanupTimeout);
        try {
            if (!await transport.ResetRegistrationAsync(timeout.Token)) {
                return cleanup;
            }
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return cleanup;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Resetting the {Provider} registration failed", cleanup.Provider);
            return cleanup;
        }

        state.RemoveAddress(cleanup.Provider);
        return cleanup with { ResetPending = false };
    }

    // A later DELETE from the same user would disable the registration that was just accepted.
    private async Task SupersedePendingCleanupAsync(string installationId, string provider) {
        var pending = await state.GetPendingCleanupAsync();
        var remaining = pending.Where(cleanup => cleanup.InstallationId != installationId || cleanup.Provider != provider).ToList();
        if (remaining.Count != pending.Count) {
            await state.SetPendingCleanupAsync(remaining);
        }
    }

    private void ScheduleRetry() {
        var delays = options.RegistrationRetryDelays;
        if (_retryAttempt >= delays.Count) {
            logger.LogInformation("Registration retries exhausted; will retry on resume, reconnect or next launch");
            return;
        }

        var delay = delays[_retryAttempt++];
        CancelRetry();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessions.SessionToken);
        lock (_sync) {
            _retryCancellation = cancellation;
            _retryTask = RetryAfterAsync(delay, cancellation.Token);
        }
    }

    private async Task RetryAfterAsync(TimeSpan delay, CancellationToken cancellationToken) {
        try {
            await Task.Delay(delay, time, cancellationToken);
            await SyncAsync(RegistrationSyncReason.Retry, cancellationToken);
        } catch (OperationCanceledException) {
            // Signed out or superseded.
        } catch (Exception ex) {
            logger.LogWarning(ex, "Registration retry failed");
        }
    }

    private void CancelRetry() {
        CancellationTokenSource? cancellation;
        lock (_sync) {
            cancellation = _retryCancellation;
            _retryCancellation = null;
        }

        cancellation?.Cancel();
    }

    private void Record(string provider, ApiResult result, string summary, string? maskedAddress) =>
        state.LastResult = new RegistrationResult(time.GetUtcNow(), provider, result.Succeeded, result.StatusCode, summary, maskedAddress);

    private void SetStatus(RegistrationStatus status) {
        lock (_sync) {
            _status = status;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Fingerprint(string userName, string provider, string installationId, string address) {
        var input = Encoding.UTF8.GetBytes($"{userName.ToLowerInvariant()}\n{provider}\n{installationId}\n{address}");
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
