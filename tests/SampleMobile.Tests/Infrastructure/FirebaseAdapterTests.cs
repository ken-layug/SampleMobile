using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Push.Firebase;

namespace SampleMobile.Tests.Infrastructure;

public class FirebaseAdapterTests {
    [Theory]
    [InlineData(ErrorCode.NotFound, MessagingErrorCode.Unregistered, DispatchOutcome.InvalidRegistration)]
    [InlineData(ErrorCode.PermissionDenied, MessagingErrorCode.SenderIdMismatch, DispatchOutcome.AuthenticationFailure)]
    [InlineData(ErrorCode.Unauthenticated, MessagingErrorCode.ThirdPartyAuthError, DispatchOutcome.AuthenticationFailure)]
    [InlineData(ErrorCode.ResourceExhausted, MessagingErrorCode.QuotaExceeded, DispatchOutcome.TransientFailure)]
    [InlineData(ErrorCode.Unavailable, MessagingErrorCode.Unavailable, DispatchOutcome.TransientFailure)]
    [InlineData(ErrorCode.Internal, MessagingErrorCode.Internal, DispatchOutcome.TransientFailure)]
    [InlineData(ErrorCode.InvalidArgument, MessagingErrorCode.InvalidArgument, DispatchOutcome.PermanentFailure)]
    [InlineData(ErrorCode.NotFound, null, DispatchOutcome.AuthenticationFailure)]
    [InlineData(ErrorCode.PermissionDenied, null, DispatchOutcome.AuthenticationFailure)]
    [InlineData(ErrorCode.DeadlineExceeded, null, DispatchOutcome.TransientFailure)]
    [InlineData(ErrorCode.FailedPrecondition, null, DispatchOutcome.PermanentFailure)]
    public void Firebase_errors_map_to_normalized_outcomes(ErrorCode errorCode, MessagingErrorCode? messagingErrorCode, DispatchOutcome expected) {
        var (outcome, code) = FirebaseErrorClassifier.Classify(errorCode, messagingErrorCode);

        Assert.Equal(expected, outcome);
        Assert.StartsWith("fcm_", code);
    }

    [Fact]
    public void Only_an_explicit_unregistered_error_marks_a_registration_invalid() {
        var invalid = Enum.GetValues<ErrorCode>()
            .SelectMany(errorCode => Enum.GetValues<MessagingErrorCode>().Cast<MessagingErrorCode?>().Append(null)
                .Select(messagingErrorCode => (errorCode, messagingErrorCode)))
            .Where(pair => FirebaseErrorClassifier.Classify(pair.errorCode, pair.messagingErrorCode).Outcome == DispatchOutcome.InvalidRegistration)
            .Select(pair => pair.messagingErrorCode)
            .Distinct()
            .ToList();

        Assert.Equal([MessagingErrorCode.Unregistered], invalid);
    }

    [Fact]
    public void The_fcm_message_carries_ids_as_data_and_collapses_duplicates() {
        var request = new PushDispatchRequest(
            Guid.CreateVersion7(),
            1,
            new PushTarget(Guid.CreateVersion7(), "fcm", DevicePlatform.Android, "fcm-registration-token"),
            new PushMessage(Guid.CreateVersion7(), "demo.alert", "Warning alert", "Disk usage high", "alert-42"));

        var message = FcmMessageFactory.Create(request, new FirebaseOptions { AndroidChannelId = "alerts" });

        var notificationId = request.Message.NotificationId.ToString();
#pragma warning disable CS0618 // Token is the documented address type for the "fcm" provider id.
        Assert.Equal("fcm-registration-token", message.Token);
#pragma warning restore CS0618
        Assert.Equal(notificationId, message.Data["notificationId"]);
        Assert.Equal("demo.alert", message.Data["type"]);
        Assert.Equal("alert-42", message.Data["resourceId"]);
        Assert.Equal(3, message.Data.Count);
        Assert.Equal("Warning alert", message.Notification.Title);
        Assert.Equal(notificationId, message.Android.CollapseKey);
        Assert.Equal(notificationId, message.Android.Notification.Tag);
        Assert.Equal("alerts", message.Android.Notification.ChannelId);
    }

    [Fact]
    public async Task Without_credentials_sends_fail_as_configuration_errors_without_network_calls() {
        var options = Options.Create(new FirebaseOptions());
        var provider = new FirebasePushNotificationProvider(
            new FirebaseMessagingClient(options, NullLogger<FirebaseMessagingClient>.Instance),
            options,
            TimeProvider.System,
            NullLogger<FirebasePushNotificationProvider>.Instance);

        var result = await provider.SendAsync(
            new PushDispatchRequest(
                Guid.CreateVersion7(),
                1,
                new PushTarget(Guid.CreateVersion7(), "fcm", DevicePlatform.Android, "token"),
                new PushMessage(Guid.CreateVersion7(), "demo.message", "Title", "Body", null)),
            TestContext.Current.CancellationToken);

        Assert.Equal(DispatchOutcome.AuthenticationFailure, result.Outcome);
        Assert.Equal("firebase_not_configured", result.ErrorCode);
        Assert.False(provider.Describe().IsConfigured);
    }
}
