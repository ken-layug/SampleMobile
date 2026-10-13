namespace SampleMobile.Application.Abstractions.Push;

/// <summary>
/// The port every push transport implements (FCM, the simulator, a future APNs or OneSignal adapter).
/// Implementations live in Infrastructure and translate the provider-neutral request into their own
/// SDK calls, then normalize whatever happens into a <see cref="PushDispatchResult"/>.
/// </summary>
/// <remarks>
/// Implementations should not throw for provider errors: classify them instead. Exceptions are
/// treated as transient failures by the dispatcher.
/// </remarks>
public interface IPushNotificationProvider {
    /// <summary>
    /// The stable identifier device registrations are stored with (for example <c>fcm</c>).
    /// A registration is only ever dispatched through the provider with exactly this id.
    /// </summary>
    string ProviderId { get; }

    /// <summary>Describes the provider for dashboards: whether it is simulated and whether it is configured.</summary>
    PushProviderDescriptor Describe();

    /// <summary>Hands one message to the provider for one device address.</summary>
    Task<PushDispatchResult> SendAsync(PushDispatchRequest request, CancellationToken cancellationToken = default);
}
