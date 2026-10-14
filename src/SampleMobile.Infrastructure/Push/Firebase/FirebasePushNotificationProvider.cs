using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Push.Firebase;

/// <summary>
/// Sends through Firebase Cloud Messaging (HTTP v1 via the Firebase Admin SDK). This is the only class
/// that calls Firebase; everything it reports goes back as a provider-neutral <see cref="PushDispatchResult"/>.
/// It is always registered, so FCM registrations stay tied to FCM even when Firebase isn't configured;
/// sends then fail with a configuration error and never touch the network.
/// </summary>
public sealed class FirebasePushNotificationProvider(
    FirebaseMessagingClient client,
    IOptions<FirebaseOptions> options,
    TimeProvider timeProvider,
    ILogger<FirebasePushNotificationProvider> logger) : IPushNotificationProvider {

    public string ProviderId => PushProviderIds.Fcm;

    public PushProviderDescriptor Describe() {
        var settings = options.Value;
        var detail = !settings.IsConfigured
            ? "Not configured: set Firebase:CredentialsPath, Firebase:CredentialsJson or Firebase:UseApplicationDefaultCredentials. FCM deliveries fail with a configuration error until then."
            : settings.ValidateOnly
                ? "Validate-only mode: FCM checks each message and token but delivers nothing."
                : $"Sending for project {(string.IsNullOrWhiteSpace(settings.ProjectId) ? "from the service account" : settings.ProjectId)}.";

        return new PushProviderDescriptor(ProviderId, "Firebase Cloud Messaging", IsSimulated: false, settings.IsConfigured, detail);
    }

    public async Task<PushDispatchResult> SendAsync(PushDispatchRequest request, CancellationToken cancellationToken = default) {
        var settings = options.Value;
        var maskedToken = TokenMasker.Mask(request.Target.Address);
        if (!settings.IsConfigured) {
            return PushDispatchResult.Failed(
                DispatchOutcome.AuthenticationFailure,
                "firebase_not_configured",
                "Firebase credentials are not configured on the server.");
        }

        FirebaseMessaging messaging;
        try {
            messaging = await client.GetMessagingAsync(cancellationToken);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            // The message names the problem (missing file, malformed key) without echoing key material.
            logger.LogError("Firebase could not be initialized: {Error}", exception.Message);
            return PushDispatchResult.Failed(
                DispatchOutcome.AuthenticationFailure,
                "firebase_credentials_invalid",
                $"Firebase could not be initialized: {exception.Message}");
        }

        try {
            var messageId = await messaging.SendAsync(FcmMessageFactory.Create(request, settings), settings.ValidateOnly, cancellationToken);
            logger.LogInformation(
                "FCM accepted job {JobId} attempt {AttemptNumber} for {Token}{Mode}",
                request.DeliveryJobId, request.AttemptNumber, maskedToken, settings.ValidateOnly ? " (validate only, not delivered)" : string.Empty);

            // Make validate-only acceptances recognizable in the delivery history.
            return PushDispatchResult.Accepted(settings.ValidateOnly ? $"validate-only:{messageId}" : messageId);
        } catch (FirebaseMessagingException exception) {
            return Failure(exception, exception.ErrorCode, exception.MessagingErrorCode, maskedToken, request);
        } catch (FirebaseException exception) {
            return Failure(exception, exception.ErrorCode, null, maskedToken, request);
        } catch (TokenResponseException exception) {
            // Google rejected the service account when exchanging it for an access token.
            logger.LogError("Firebase authentication failed: {Error}", exception.Error?.Error);
            return PushDispatchResult.Failed(
                DispatchOutcome.AuthenticationFailure,
                "firebase_auth_rejected",
                $"Google rejected the service-account credentials ({exception.Error?.Error}).");
        } catch (HttpRequestException exception) {
            logger.LogWarning("Network error calling FCM for job {JobId}: {Error}", request.DeliveryJobId, exception.Message);
            return PushDispatchResult.Failed(DispatchOutcome.TransientFailure, "fcm_network_error", exception.Message);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return PushDispatchResult.Failed(DispatchOutcome.TransientFailure, "fcm_timeout", "The request to FCM timed out.");
        }
    }

    private PushDispatchResult Failure(
        FirebaseException exception,
        ErrorCode errorCode,
        MessagingErrorCode? messagingErrorCode,
        string maskedToken,
        PushDispatchRequest request) {
        var (outcome, code) = FirebaseErrorClassifier.Classify(errorCode, messagingErrorCode);
        var retryAfter = RetryAfter(exception.HttpResponse);

        logger.LogWarning(
            "FCM rejected job {JobId} attempt {AttemptNumber} for {Token}: {ErrorCode}/{MessagingErrorCode} -> {Outcome}",
            request.DeliveryJobId, request.AttemptNumber, maskedToken, errorCode, messagingErrorCode, outcome);

        return PushDispatchResult.Failed(outcome, code, exception.Message, retryAfter: retryAfter);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage? response) {
        var header = response?.Headers.RetryAfter;
        if (header?.Delta is { } delta) {
            return delta;
        }

        return header?.Date is { } date && date > timeProvider.GetUtcNow() ? date - timeProvider.GetUtcNow() : null;
    }
}
