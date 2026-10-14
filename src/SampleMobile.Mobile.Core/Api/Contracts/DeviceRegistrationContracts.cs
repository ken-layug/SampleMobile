namespace SampleMobile.Mobile.Core.Api.Contracts;

/// <summary>
/// Body of <c>POST /api/mobile/device-registrations</c>. <see cref="InstallationId"/> is app-owned and stable;
/// <see cref="Address"/> is the provider's (rotating) registration token.
/// </summary>
public sealed record RegisterDeviceRequest(string InstallationId, string Platform, string Provider, string Address);

/// <summary>A registration as the backend reports it. The provider address is never returned.</summary>
public sealed record DeviceRegistrationResponse(
    Guid Id,
    string InstallationId,
    string Platform,
    string Provider,
    bool Enabled,
    string? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastSeenAt);
