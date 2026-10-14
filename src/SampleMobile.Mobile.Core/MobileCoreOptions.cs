namespace SampleMobile.Mobile.Core;

/// <summary>Settings the host app supplies when it registers the core services.</summary>
public sealed class MobileCoreOptions {
    /// <summary>The <c>platform</c> value sent with device registrations (<c>android</c>, <c>ios</c>, <c>virtual</c>).</summary>
    public string Platform { get; set; } = "android";

    /// <summary>Backend base URL used until the user enters another one on the login screen. Empty means "ask".</summary>
    public string DefaultBackendUrl { get; set; } = string.Empty;

    /// <summary>Timeout for a single API request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Creates the primary HTTP handler. Android passes its native handler so the app's network security
    /// config (cleartext rules, trust anchors) applies. Null keeps the framework default.
    /// </summary>
    public Func<HttpMessageHandler>? CreatePrimaryHttpHandler { get; set; }

    /// <summary>Delays between registration retries after a transient failure. Bounded; this is not polling.</summary>
    public IReadOnlyList<TimeSpan> RegistrationRetryDelays { get; set; } = [TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];
}
