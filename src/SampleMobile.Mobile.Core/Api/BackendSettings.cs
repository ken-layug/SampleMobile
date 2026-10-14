using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Core.Api;

/// <summary>Where the backend lives. Changeable at runtime from the login screen (development).</summary>
public interface IBackendSettings {
    /// <summary>Absolute base URI ending in <c>/</c>, or null when none is configured.</summary>
    Uri? BaseUri { get; }

    /// <summary>Validates and stores a new base URL. Returns an error message when it is unusable.</summary>
    string? TrySetBaseUrl(string? value);
}

/// <inheritdoc />
public sealed class BackendSettings(IAppPreferences preferences, MobileCoreOptions options) : IBackendSettings {
    internal const string PreferenceKey = "backend.baseUrl";

    public Uri? BaseUri => Normalize(preferences.Get(PreferenceKey)) ?? Normalize(options.DefaultBackendUrl);

    public string? TrySetBaseUrl(string? value) {
        var uri = Normalize(value);
        if (uri is null) {
            return "Enter an absolute http:// or https:// URL, for example http://10.0.2.2:5010.";
        }

        preferences.Set(PreferenceKey, uri.ToString());
        return null;
    }

    internal static Uri? Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) {
            return null;
        }

        // Relative paths resolve against the last segment, so the base must end in a slash.
        var text = uri.GetLeftPart(UriPartial.Path);
        return new Uri(text.EndsWith('/') ? text : text + "/");
    }
}
