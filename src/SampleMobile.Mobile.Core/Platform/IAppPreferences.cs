namespace SampleMobile.Mobile.Core.Platform;

/// <summary>Non-secret app settings (backend URL, installation id, sync bookkeeping).</summary>
public interface IAppPreferences {
    string? Get(string key);

    void Set(string key, string value);

    void Remove(string key);
}
