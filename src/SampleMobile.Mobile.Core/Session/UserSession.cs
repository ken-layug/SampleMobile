namespace SampleMobile.Mobile.Core.Session;

/// <summary>
/// The signed-in user as far as the app knows. <see cref="UserName"/> is what the user typed; the backend
/// identifies the user from the token's <c>sub</c> claim, which the app treats as opaque.
/// </summary>
public sealed record UserSession(string UserName, string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>Why the session changed.</summary>
public enum SessionChangeKind {
    SignedIn,
    SignedOut,
    /// <summary>The backend rejected the token (401) or it passed its expiry time.</summary>
    Expired
}

/// <summary>Raised by <see cref="ISessionStore.Changed"/>.</summary>
public sealed class SessionChangedEventArgs(SessionChangeKind kind, string userName) : EventArgs {
    public SessionChangeKind Kind { get; } = kind;

    public string UserName { get; } = userName;
}
