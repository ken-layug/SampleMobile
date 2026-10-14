namespace SampleMobile.Mobile.Core.Api;

/// <summary>Per-request flags read by <see cref="BearerTokenHandler"/>.</summary>
public static class ApiRequestOptions {
    /// <summary>The request must not carry a bearer token (login).</summary>
    public static readonly HttpRequestOptionsKey<bool> Anonymous = new("SampleMobile.Anonymous");

    /// <summary>Use this token instead of the current session's (deferred logout cleanup).</summary>
    public static readonly HttpRequestOptionsKey<string> AccessTokenOverride = new("SampleMobile.AccessTokenOverride");
}
