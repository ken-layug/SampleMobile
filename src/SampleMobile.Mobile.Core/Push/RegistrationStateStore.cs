using System.Text.Json;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Core.Push;

/// <summary>
/// Work left over from a sign-out that couldn't finish (usually offline). <see cref="DeletePending"/> needs the
/// signed-out user's token, which expires; <see cref="ResetPending"/> invalidates the provider address locally and
/// is the revocation that still works after that.
/// </summary>
public sealed record PendingCleanup(
    string InstallationId,
    string Provider,
    string? AccessToken,
    DateTimeOffset? TokenExpiresAt,
    bool DeletePending,
    bool ResetPending,
    DateTimeOffset CreatedAt) {
    public bool IsComplete => !DeletePending && !ResetPending;
}

/// <summary>The last registration attempt, for status screens. The address is masked.</summary>
public sealed record RegistrationResult(
    DateTimeOffset At,
    string Provider,
    bool Succeeded,
    int? StatusCode,
    string Summary,
    string? MaskedAddress);

/// <summary>
/// Persisted registration bookkeeping. Provider addresses and pending cleanup (which holds an access token)
/// go to secure storage; the rest is non-secret.
/// </summary>
public sealed class RegistrationStateStore(ISecureStore secureStore, IAppPreferences preferences) {
    private const string AddressKeyPrefix = "push.address.";
    private const string PendingCleanupKey = "push.pendingCleanup";
    private const string FingerprintKey = "push.registration.fingerprint";
    private const string RegisteredProviderKey = "push.registration.provider";
    private const string DirtyKey = "push.registration.dirty";
    private const string LastResultKey = "push.registration.lastResult";

    /// <summary>Hash of user, provider, installation and address last accepted by the backend.</summary>
    public string? Fingerprint {
        get => preferences.Get(FingerprintKey);
        set => SetOrRemove(FingerprintKey, value);
    }

    /// <summary>The provider of the registration the backend last accepted from this installation.</summary>
    public string? RegisteredProvider {
        get => preferences.Get(RegisteredProviderKey);
        set => SetOrRemove(RegisteredProviderKey, value);
    }

    /// <summary>A sync failed transiently and must be retried (on resume, connectivity or the next launch).</summary>
    public bool Dirty {
        get => preferences.Get(DirtyKey) == "true";
        set => SetOrRemove(DirtyKey, value ? "true" : null);
    }

    public RegistrationResult? LastResult {
        get => Read(preferences.Get(LastResultKey), MobileStateJsonContext.Default.RegistrationResult);
        set => SetOrRemove(LastResultKey, value is null ? null : JsonSerializer.Serialize(value, MobileStateJsonContext.Default.RegistrationResult));
    }

    public Task<string?> GetAddressAsync(string provider) => secureStore.GetAsync(AddressKeyPrefix + provider);

    public Task SetAddressAsync(string provider, string address) => secureStore.SetAsync(AddressKeyPrefix + provider, address);

    public void RemoveAddress(string provider) => secureStore.Remove(AddressKeyPrefix + provider);

    public async Task<IReadOnlyList<PendingCleanup>> GetPendingCleanupAsync() =>
        Read(await secureStore.GetAsync(PendingCleanupKey), MobileStateJsonContext.Default.ListPendingCleanup) ?? [];

    public async Task SetPendingCleanupAsync(IReadOnlyCollection<PendingCleanup> pending) {
        if (pending.Count == 0) {
            secureStore.Remove(PendingCleanupKey);
            return;
        }

        await secureStore.SetAsync(PendingCleanupKey, JsonSerializer.Serialize(pending.ToList(), MobileStateJsonContext.Default.ListPendingCleanup));
    }

    private void SetOrRemove(string key, string? value) {
        if (value is null) {
            preferences.Remove(key);
        } else {
            preferences.Set(key, value);
        }
    }

    private static T? Read<T>(string? json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class {
        if (string.IsNullOrEmpty(json)) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize(json, typeInfo);
        } catch (JsonException) {
            return null;
        }
    }
}
