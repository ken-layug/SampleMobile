using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Abstractions.Push;

/// <summary>Status of a registered provider, as shown on the dashboard.</summary>
public sealed record PushProviderDescriptor(
    string ProviderId,
    string DisplayName,
    bool IsSimulated,
    bool IsConfigured,
    string? StatusDetail);

/// <summary>Where to send: one device registration and its provider-specific address.</summary>
/// <remarks><see cref="Address"/> is a credential-like token. Never log it unmasked.</remarks>
public sealed record PushTarget(
    Guid RegistrationId,
    string ProviderId,
    DevicePlatform Platform,
    string Address);

/// <summary>
/// The provider-neutral push content. <see cref="NotificationId"/>, <see cref="Type"/> and
/// <see cref="ResourceId"/> form the data payload the app uses to fetch the authoritative record
/// from the inbox API; <see cref="Title"/> and <see cref="Body"/> are only display text.
/// </summary>
public sealed record PushMessage(
    Guid NotificationId,
    string Type,
    string Title,
    string Body,
    string? ResourceId);

/// <summary>One dispatch attempt for one delivery job.</summary>
public sealed record PushDispatchRequest(
    Guid DeliveryJobId,
    int AttemptNumber,
    PushTarget Target,
    PushMessage Message);

/// <summary>
/// The normalized result of a dispatch. <see cref="DispatchOutcome.Accepted"/> means the provider
/// accepted the message, not that a device displayed it.
/// </summary>
public sealed record PushDispatchResult(
    DispatchOutcome Outcome,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsSimulated,
    TimeSpan? RetryAfter) {
    public static PushDispatchResult Accepted(string? providerMessageId, bool isSimulated = false) =>
        new(DispatchOutcome.Accepted, providerMessageId, null, null, isSimulated, null);

    public static PushDispatchResult Failed(
        DispatchOutcome outcome,
        string errorCode,
        string errorMessage,
        bool isSimulated = false,
        TimeSpan? retryAfter = null) {
        if (outcome == DispatchOutcome.Accepted) {
            throw new ArgumentException("A failed result cannot have an accepted outcome.", nameof(outcome));
        }

        return new PushDispatchResult(outcome, null, errorCode, errorMessage, isSimulated, retryAfter);
    }
}
