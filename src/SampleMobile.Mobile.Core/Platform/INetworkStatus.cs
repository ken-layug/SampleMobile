namespace SampleMobile.Mobile.Core.Platform;

/// <summary>Connectivity as the OS reports it. Used to retry deferred work, never to poll.</summary>
public interface INetworkStatus {
    bool IsOnline { get; }

    /// <summary>Raised when the device goes from offline to online.</summary>
    event EventHandler? ConnectivityRestored;
}
