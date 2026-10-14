using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Core.Push;

/// <summary>
/// The app-owned, stable id of this installation. It is generated once and never derived from a push token,
/// which rotates. (Android Auto Backup is disabled so the id is not cloned onto another device.)
/// </summary>
public sealed class InstallationIdProvider(IAppPreferences preferences) {
    internal const string PreferenceKey = "installationId";

    private readonly Lock _sync = new();

    public string GetOrCreate() {
        lock (_sync) {
            var existing = preferences.Get(PreferenceKey);
            if (Guid.TryParse(existing, out var id)) {
                return id.ToString("D");
            }

            var created = Guid.NewGuid().ToString("D");
            preferences.Set(PreferenceKey, created);
            return created;
        }
    }
}
