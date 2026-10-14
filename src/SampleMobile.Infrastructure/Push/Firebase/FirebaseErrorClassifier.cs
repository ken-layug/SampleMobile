using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Push.Firebase;

/// <summary>
/// Maps Firebase error codes onto the application's normalized outcomes. Works on the codes rather than
/// the exception (whose constructors are internal), so the mapping is unit-testable.
///
/// The asymmetry is deliberate: only an explicit <c>UNREGISTERED</c> marks a device invalid. Errors that
/// could also mean "our project or credentials are wrong" (SENDER_ID_MISMATCH, NOT_FOUND, PERMISSION_DENIED)
/// are configuration failures, so a misconfiguration can never mass-disable valid devices.
/// </summary>
internal static class FirebaseErrorClassifier {
    public static (DispatchOutcome Outcome, string Code) Classify(ErrorCode errorCode, MessagingErrorCode? messagingErrorCode) =>
        messagingErrorCode switch {
            MessagingErrorCode.Unregistered => (DispatchOutcome.InvalidRegistration, "fcm_unregistered"),
            MessagingErrorCode.SenderIdMismatch => (DispatchOutcome.AuthenticationFailure, "fcm_sender_id_mismatch"),
            MessagingErrorCode.ThirdPartyAuthError => (DispatchOutcome.AuthenticationFailure, "fcm_third_party_auth_error"),
            MessagingErrorCode.QuotaExceeded => (DispatchOutcome.TransientFailure, "fcm_quota_exceeded"),
            MessagingErrorCode.Unavailable => (DispatchOutcome.TransientFailure, "fcm_unavailable"),
            MessagingErrorCode.Internal => (DispatchOutcome.TransientFailure, "fcm_internal"),
            MessagingErrorCode.InvalidArgument => (DispatchOutcome.PermanentFailure, "fcm_invalid_argument"),
            _ => ClassifyPlatformError(errorCode)
        };

    private static (DispatchOutcome Outcome, string Code) ClassifyPlatformError(ErrorCode errorCode) =>
        errorCode switch {
            ErrorCode.Unauthenticated or ErrorCode.PermissionDenied or ErrorCode.NotFound =>
                (DispatchOutcome.AuthenticationFailure, $"fcm_{Snake(errorCode)}"),
            ErrorCode.Unavailable or ErrorCode.Internal or ErrorCode.DeadlineExceeded
                or ErrorCode.ResourceExhausted or ErrorCode.Aborted or ErrorCode.Unknown =>
                (DispatchOutcome.TransientFailure, $"fcm_{Snake(errorCode)}"),
            _ => (DispatchOutcome.PermanentFailure, $"fcm_{Snake(errorCode)}")
        };

    private static string Snake(ErrorCode errorCode) =>
        string.Concat(errorCode.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}
