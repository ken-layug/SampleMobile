using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Services;

/// <summary>Secrets in MAUI <c>SecureStorage</c> (Android Keystore + encrypted shared preferences).</summary>
public sealed class MauiSecureStore(ILogger<MauiSecureStore> logger) : ISecureStore {
    public async Task<string?> GetAsync(string key) {
        try {
            return await SecureStorage.Default.GetAsync(key);
        } catch (Exception ex) {
            // Values restored from another device (or a reinstall) can't be decrypted; start clean.
            logger.LogWarning(ex, "Secure storage could not be read; clearing it");
            SecureStorage.Default.RemoveAll();
            return null;
        }
    }

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}

/// <summary>Non-secret settings in MAUI <c>Preferences</c>.</summary>
public sealed class MauiPreferences : IAppPreferences {
    public string? Get(string key) => Preferences.Default.Get<string?>(key, null);

    public void Set(string key, string value) => Preferences.Default.Set(key, value);

    public void Remove(string key) => Preferences.Default.Remove(key);
}

/// <summary>Marshals view-model updates onto the UI thread.</summary>
public sealed class MauiDispatcher : IMainThreadDispatcher {
    public void Dispatch(Action action) {
        if (MainThread.IsMainThread) {
            action();
        } else {
            MainThread.BeginInvokeOnMainThread(action);
        }
    }
}

/// <summary>Raises <see cref="ConnectivityRestored"/> when the device goes from offline to online.</summary>
public sealed class MauiNetworkStatus : INetworkStatus {
    private bool _online;

    public MauiNetworkStatus() {
        _online = IsOnline;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event EventHandler? ConnectivityRestored;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) {
        var online = e.NetworkAccess == NetworkAccess.Internet;
        var restored = online && !_online;
        _online = online;
        if (restored) {
            ConnectivityRestored?.Invoke(this, EventArgs.Empty);
        }
    }
}
