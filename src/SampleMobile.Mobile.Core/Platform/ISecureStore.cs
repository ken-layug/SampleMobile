namespace SampleMobile.Mobile.Core.Platform;

/// <summary>
/// Encrypted key/value storage for secrets (access token, provider address, pending cleanup).
/// On Android this is MAUI <c>SecureStorage</c> (Keystore-backed); never plain preferences.
/// </summary>
public interface ISecureStore {
    /// <summary>Returns the value, or null when it is missing or can no longer be decrypted.</summary>
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}
