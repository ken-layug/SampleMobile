using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Contracts;

/// <summary>Registers (or refreshes) this app installation for push through one provider.</summary>
public sealed record RegisterDeviceRequest {
    /// <summary>A stable id the app generates once per installation (a UUID is recommended). Not the push token.</summary>
    [Required]
    [MaxLength(DeviceRegistration.MaxInstallationIdLength)]
    public string? InstallationId { get; init; }

    /// <summary><c>android</c>, <c>ios</c> or <c>virtual</c>.</summary>
    [Required]
    [MaxLength(20)]
    public string? Platform { get; init; }

    /// <summary>The push provider the address belongs to, e.g. <c>fcm</c>. Must be a provider the server supports.</summary>
    [Required]
    [MaxLength(DeviceRegistration.MaxProviderIdLength)]
    public string? Provider { get; init; }

    /// <summary>The provider-specific address, e.g. the FCM registration token. Treated as a secret.</summary>
    [Required]
    [MaxLength(DeviceRegistration.MaxAddressLength)]
    public string? Address { get; init; }
}

/// <summary>A device registration as returned to its owner. The provider address is never echoed back.</summary>
/// <param name="Id">Server-side registration id.</param>
/// <param name="InstallationId">The app-generated installation id.</param>
/// <param name="Platform"><c>android</c>, <c>ios</c> or <c>virtual</c>.</param>
/// <param name="Provider">The provider id, e.g. <c>fcm</c>.</param>
/// <param name="Enabled">Whether pushes are currently sent to this registration.</param>
/// <param name="DisabledReason"><c>unregistered</c>, <c>invalidatedByProvider</c> or <c>tokenReassigned</c> when disabled.</param>
/// <param name="CreatedAt">When the installation first registered.</param>
/// <param name="UpdatedAt">When the registration last changed (token rotation, re-enable, owner change).</param>
/// <param name="LastSeenAt">When the app last registered.</param>
public sealed record DeviceRegistrationResponse(
    Guid Id,
    string InstallationId,
    string Platform,
    string Provider,
    bool Enabled,
    string? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastSeenAt) {
    public static DeviceRegistrationResponse FromEntity(DeviceRegistration registration) =>
        new(
            registration.Id,
            registration.InstallationId,
            DevicePlatforms.ToWire(registration.Platform),
            registration.ProviderId,
            registration.IsEnabled,
            registration.DisabledReason is { } reason ? JsonNamingPolicy.CamelCase.ConvertName(reason.ToString()) : null,
            registration.CreatedAt,
            registration.UpdatedAt,
            registration.LastSeenAt);
}

/// <summary>Wire names for <see cref="DevicePlatform"/>.</summary>
public static class DevicePlatforms {
    public static string ToWire(DevicePlatform platform) => platform.ToString().ToLowerInvariant();

    public static bool TryParse(string? value, out DevicePlatform platform) {
        platform = default;
        // Enum.TryParse would also accept numeric strings such as "1"; only names are part of the contract.
        return !string.IsNullOrWhiteSpace(value)
            && value.Trim().All(char.IsLetter)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out platform)
            && Enum.IsDefined(platform);
    }
}
