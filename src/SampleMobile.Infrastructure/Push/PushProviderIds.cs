namespace SampleMobile.Infrastructure.Push;

/// <summary>
/// Provider ids as stored on device registrations. Only Infrastructure names concrete providers;
/// the Application layer treats a provider id as an opaque string.
/// </summary>
public static class PushProviderIds {
    public const string Fcm = "fcm";
    public const string Simulated = "simulated";
}
