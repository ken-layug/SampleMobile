using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Push.Simulated;

public sealed class SimulatedProviderOptions {
    public const string SectionName = "Push:Simulated";

    /// <summary>Artificial provider latency, so the dashboard can show jobs in flight.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// A push provider that never leaves the process: no Firebase account, credentials or network.
/// "Accepted" here means only that the simulator accepted the message — no device is involved, and
/// every result is flagged <see cref="PushDispatchResult.IsSimulated"/> so it is never mistaken for a real delivery.
/// </summary>
public sealed class SimulatedPushNotificationProvider(
    SimulatedPushControl control,
    IOptions<SimulatedProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<SimulatedPushNotificationProvider> logger) : IPushNotificationProvider {

    public string ProviderId => PushProviderIds.Simulated;

    public PushProviderDescriptor Describe() =>
        new(ProviderId, "Simulated provider (no real devices)", IsSimulated: true, IsConfigured: true,
            "In-process simulator; outcomes can be scripted from the dashboard.");

    public async Task<PushDispatchResult> SendAsync(PushDispatchRequest request, CancellationToken cancellationToken = default) {
        if (options.Value.Latency > TimeSpan.Zero) {
            await Task.Delay(options.Value.Latency, cancellationToken);
        }

        var outcome = control.NextOutcomeFor(request.Target.Address);
        var simulatedMessageId = $"sim-{Guid.CreateVersion7():N}";
        var result = outcome switch {
            DispatchOutcome.Accepted => PushDispatchResult.Accepted(simulatedMessageId, isSimulated: true),
            DispatchOutcome.TransientFailure => PushDispatchResult.Failed(
                outcome, "simulated_unavailable", "Simulated transient failure (provider temporarily unavailable).", isSimulated: true),
            DispatchOutcome.InvalidRegistration => PushDispatchResult.Failed(
                outcome, "simulated_unregistered", "Simulated provider reports the device address is no longer registered.", isSimulated: true),
            DispatchOutcome.AuthenticationFailure => PushDispatchResult.Failed(
                outcome, "simulated_auth_error", "Simulated authentication failure.", isSimulated: true),
            _ => PushDispatchResult.Failed(
                DispatchOutcome.PermanentFailure, "simulated_rejected", "Simulated permanent rejection of the message.", isSimulated: true)
        };

        control.Record(new SimulatedPushRecord(
            simulatedMessageId,
            timeProvider.GetUtcNow(),
            request.DeliveryJobId,
            request.AttemptNumber,
            request.Message.NotificationId,
            request.Message.Type,
            request.Message.Title,
            TokenMasker.Mask(request.Target.Address),
            result.Outcome,
            result.ErrorCode));

        logger.LogInformation(
            "[SIMULATED] Job {JobId} attempt {AttemptNumber} to {Address}: {Outcome}",
            request.DeliveryJobId, request.AttemptNumber, TokenMasker.Mask(request.Target.Address), result.Outcome);

        return result;
    }
}
