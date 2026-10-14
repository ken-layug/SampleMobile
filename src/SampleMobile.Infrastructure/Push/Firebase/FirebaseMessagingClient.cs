using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SampleMobile.Infrastructure.Push.Firebase;

/// <summary>
/// Owns this app's <see cref="FirebaseApp"/>. It is created on first use rather than at startup, so the
/// application runs without Firebase configured, and a failed initialization (bad path, malformed key)
/// is retried on the next send instead of being cached for the life of the process.
/// A named app is used so nothing else in the process can collide with the SDK's global default app.
/// </summary>
public sealed class FirebaseMessagingClient(
    IOptions<FirebaseOptions> options,
    ILogger<FirebaseMessagingClient> logger) : IDisposable {
    private const string AppName = "sample-mobile";

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private FirebaseMessaging? _messaging;
    private FirebaseApp? _app;

    public async Task<FirebaseMessaging> GetMessagingAsync(CancellationToken cancellationToken) {
        if (_messaging is { } messaging) {
            return messaging;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try {
            if (_messaging is null) {
                _app = FirebaseApp.GetInstance(AppName) ?? FirebaseApp.Create(await CreateOptionsAsync(cancellationToken), AppName);
                _messaging = FirebaseMessaging.GetMessaging(_app);
                logger.LogInformation("Firebase Admin SDK initialized for project {ProjectId}", _app.Options.ProjectId ?? "(from credentials)");
            }

            return _messaging;
        } finally {
            _initializationLock.Release();
        }
    }

    private async Task<AppOptions> CreateOptionsAsync(CancellationToken cancellationToken) {
        var settings = options.Value;
        GoogleCredential credential;
        if (!string.IsNullOrWhiteSpace(settings.CredentialsJson)) {
            credential = CredentialFactory.FromJson<ServiceAccountCredential>(settings.CredentialsJson).ToGoogleCredential();
        } else if (!string.IsNullOrWhiteSpace(settings.CredentialsPath)) {
            credential = CredentialFactory.FromFile<ServiceAccountCredential>(settings.CredentialsPath).ToGoogleCredential();
        } else if (settings.UseApplicationDefaultCredentials) {
            credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken);
        } else {
            throw new InvalidOperationException("Firebase is not configured.");
        }

        return new AppOptions {
            Credential = credential,
            ProjectId = string.IsNullOrWhiteSpace(settings.ProjectId) ? null : settings.ProjectId
        };
    }

    public void Dispose() {
        _app?.Delete();
        _initializationLock.Dispose();
    }
}
