using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Core.Session;

/// <summary>Why the signed-in pipeline is being (re)started.</summary>
public enum BootstrapReason {
    Login,
    AppStart
}

/// <summary>
/// Brings a signed-in session to a working state: transport, installation id, permission, provider address,
/// backend registration, inbox. Every step tolerates failure (the UI shows a status instead), and the whole
/// run stops when the session ends.
/// </summary>
public sealed class SessionBootstrapper(
    ISessionStore sessions,
    PushTransportSelector selector,
    InstallationIdProvider installation,
    INotificationPermissionService permissions,
    DeviceRegistrationSync registration,
    INotificationInboxService inbox,
    ILogger<SessionBootstrapper> logger) {
    private static readonly TimeSpan AddressTimeout = TimeSpan.FromSeconds(30);

    private readonly Lock _sync = new();
    private Task? _running;
    private long _runningGeneration;
    private PushAddressResult? _lastAddressResult;

    public event EventHandler? Changed;

    /// <summary>The last attempt to obtain the provider address (its failure reason is shown in diagnostics).</summary>
    public PushAddressResult? LastAddressResult {
        get {
            lock (_sync) {
                return _lastAddressResult;
            }
        }
    }

    /// <summary>Starts the pipeline unless it is already running for this session.</summary>
    public Task RunAsync(BootstrapReason reason, CancellationToken cancellationToken = default) {
        var generation = sessions.Generation;
        lock (_sync) {
            if (_running is { IsCompleted: false } && _runningGeneration == generation) {
                return _running;
            }

            _runningGeneration = generation;
            _running = RunCoreAsync(reason, cancellationToken);
            return _running;
        }
    }

    /// <summary>
    /// The app came back to the foreground or the network returned: finish deferred work (sign-out cleanup,
    /// a missing address, a registration that failed transiently) and refresh the inbox. Never polls.
    /// </summary>
    public async Task ResumeAsync(RegistrationSyncReason reason, CancellationToken cancellationToken = default) {
        try {
            await registration.ProcessPendingCleanupAsync(beforeRegistration: false, cancellationToken);
            if (await sessions.GetActiveAsync(cancellationToken) is null || !selector.IsSelected) {
                return;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessions.SessionToken);
            if (LastAddressResult is { Succeeded: false, Failure: PushAddressFailure.Transient }) {
                await ObtainAddressAsync(selector.Active, linked.Token);
            }

            await registration.SyncAsync(reason, linked.Token);
            await inbox.RefreshAsync(linked.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // The session ended meanwhile.
        }
    }

    /// <summary>Manual refresh (diagnostics): asks the provider for its address again and re-registers unconditionally.</summary>
    public async Task<RegistrationSyncOutcome> RefreshRegistrationAsync(CancellationToken cancellationToken = default) {
        if (await sessions.GetActiveAsync(cancellationToken) is null) {
            return RegistrationSyncOutcome.Skipped;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessions.SessionToken);
        if (!selector.IsSelected) {
            await selector.SelectAsync(linked.Token);
        }

        await ObtainAddressAsync(selector.Active, linked.Token);
        return await registration.SyncAsync(RegistrationSyncReason.Manual, linked.Token);
    }

    private async Task RunCoreAsync(BootstrapReason reason, CancellationToken cancellationToken) {
        await Task.Yield();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessions.SessionToken);
        var token = linked.Token;
        try {
            // 1. Leftover sign-out cleanup first: a pending provider reset must not delete the address about to be registered.
            await registration.ProcessPendingCleanupAsync(beforeRegistration: true, token);

            // 2. Select and initialize the transport (falls back to inbox-only), then allow address issuance.
            var transport = await selector.SelectAsync(token);
            await transport.ActivateAsync(token);
            logger.LogInformation("Push transport: {Provider} ({Status})", transport.ProviderId, selector.ActiveStatus);

            // 3. The app-owned installation id.
            installation.GetOrCreate();

            // 4. Ask for notification permission. Nothing below depends on the answer, so it isn't awaited.
            _ = RequestPermissionAsync();

            // 5. The provider's current address, persisted before any network call to the backend.
            await ObtainAddressAsync(transport, token);

            // 6. Register (the latest persisted address; a failure schedules bounded retries).
            await registration.SyncAsync(reason == BootstrapReason.Login ? RegistrationSyncReason.Login : RegistrationSyncReason.AppStart, token);

            // 7. The inbox, from the backend.
            await inbox.RefreshAsync(token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            logger.LogInformation("Session ended during start-up work");
        } catch (Exception ex) {
            logger.LogError(ex, "Session start-up work failed");
        }
    }

    private async Task ObtainAddressAsync(IPushTransport transport, CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AddressTimeout);
        PushAddressResult result;
        try {
            result = await transport.GetRegistrationAddressAsync(timeout.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            result = PushAddressResult.Transient("Timed out waiting for the push provider.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            result = PushAddressResult.Transient(ex.Message);
        }

        if (result.Succeeded) {
            await registration.StoreAddressAsync(transport.ProviderId, result.Address!, cancellationToken);
        } else {
            logger.LogWarning("{Provider} gave no address: {Failure} {Reason}", transport.ProviderId, result.Failure, result.Reason);
        }

        lock (_sync) {
            _lastAddressResult = result;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RequestPermissionAsync() {
        try {
            await permissions.RequestIfNeededAsync();
        } catch (Exception ex) {
            logger.LogWarning(ex, "Requesting notification permission failed");
        }
    }
}
