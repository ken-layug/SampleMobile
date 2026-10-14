namespace SampleMobile.Infrastructure.Push.Firebase;

/// <summary>
/// Firebase Cloud Messaging settings, bound from the <c>Firebase</c> section. Credentials never live in
/// appsettings files: point <see cref="CredentialsPath"/> at a service-account JSON kept outside the repo,
/// put the JSON itself in user-secrets/an environment variable (<see cref="CredentialsJson"/>), or use
/// Google application default credentials.
/// </summary>
public sealed class FirebaseOptions {
    public const string SectionName = "Firebase";

    /// <summary>Optional; defaults to the project in the service-account credentials.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Path to a service-account JSON file (outside the repository).</summary>
    public string? CredentialsPath { get; set; }

    /// <summary>The service-account JSON itself, e.g. from user-secrets or <c>Firebase__CredentialsJson</c>.</summary>
    public string? CredentialsJson { get; set; }

    /// <summary>Use Google application default credentials (<c>GOOGLE_APPLICATION_CREDENTIALS</c>, workload identity).</summary>
    public bool UseApplicationDefaultCredentials { get; set; }

    /// <summary>
    /// FCM "dry run": Firebase validates the message and token but delivers nothing. Useful to check
    /// credentials and a device token without a phone in hand.
    /// </summary>
    public bool ValidateOnly { get; set; }

    /// <summary>Android notification channel id the app creates; empty uses the app's default channel.</summary>
    public string? AndroidChannelId { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CredentialsPath)
        || !string.IsNullOrWhiteSpace(CredentialsJson)
        || UseApplicationDefaultCredentials;
}
