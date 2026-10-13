using SampleMobile.Domain.Primitives;

namespace SampleMobile.Domain.Entities;

public enum DevicePlatform {
    Android,
    Ios,

    /// <summary>A virtual device that only exists for the simulated provider.</summary>
    Virtual
}

public enum DeviceDisabledReason {
    /// <summary>The app unregistered the device (logout, user opted out).</summary>
    Unregistered,

    /// <summary>The provider explicitly confirmed that the address is no longer valid.</summary>
    InvalidatedByProvider,

    /// <summary>The same provider address was registered by another installation.</summary>
    TokenReassigned
}

/// <summary>
/// A user's app installation that can receive push through one specific provider.
/// The stable <see cref="InstallationId"/> is the application's identity for the install;
/// <see cref="Address"/> is the provider-specific token, which rotates and is sensitive.
/// A registration can only ever be dispatched through the provider it was registered for.
/// </summary>
public sealed class DeviceRegistration : AggregateRoot {
    public const int MaxInstallationIdLength = 128;
    public const int MaxProviderIdLength = 50;
    public const int MaxAddressLength = 1024;

    private DeviceRegistration() {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string InstallationId { get; private set; } = string.Empty;
    public DevicePlatform Platform { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public DeviceDisabledReason? DisabledReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Provider ids are compared case-insensitively; store them in one canonical form.</summary>
    public static string NormalizeProviderId(string providerId) => providerId.Trim().ToLowerInvariant();

    public static DeviceRegistration Register(
        Guid userId,
        string installationId,
        DevicePlatform platform,
        string providerId,
        string address,
        DateTimeOffset now) {
        if (userId == Guid.Empty) {
            throw new ArgumentException("A registration must belong to a user.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        return new DeviceRegistration {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            InstallationId = installationId.Trim(),
            Platform = platform,
            ProviderId = NormalizeProviderId(providerId),
            Address = address.Trim(),
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            LastSeenAt = now
        };
    }

    /// <summary>
    /// Applies a repeated registration for the same installation and provider: the signed-in
    /// user takes ownership, a rotated token replaces the old one, and the registration is
    /// re-enabled because the app has just proven it is alive.
    /// </summary>
    /// <returns><c>true</c> when ownership moved to a different user.</returns>
    public bool Refresh(Guid userId, DevicePlatform platform, string address, DateTimeOffset now) {
        if (userId == Guid.Empty) {
            throw new ArgumentException("A registration must belong to a user.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var ownershipTransferred = UserId != userId;
        var normalizedAddress = address.Trim();
        var changed = ownershipTransferred
            || Platform != platform
            || !string.Equals(Address, normalizedAddress, StringComparison.Ordinal)
            || !IsEnabled;

        UserId = userId;
        Platform = platform;
        Address = normalizedAddress;
        IsEnabled = true;
        DisabledReason = null;
        LastSeenAt = now;
        if (changed) {
            UpdatedAt = now;
        }

        return ownershipTransferred;
    }

    /// <summary>Stops dispatching to this registration. Idempotent; the row is kept for history.</summary>
    public void Disable(DeviceDisabledReason reason, DateTimeOffset now) {
        if (!IsEnabled) {
            return;
        }

        IsEnabled = false;
        DisabledReason = reason;
        UpdatedAt = now;
    }
}
