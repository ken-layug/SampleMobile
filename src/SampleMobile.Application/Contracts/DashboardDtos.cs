using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Contracts;

// Read models for the in-process admin dashboard. They are not part of the mobile wire contract.

public sealed record DashboardOverview(DashboardStats Stats, IReadOnlyList<PushProviderDescriptor> Providers);

/// <summary>A device registration for the admin screen, with the provider address masked.</summary>
public sealed record AdminDeviceRegistrationItem(
    Guid RegistrationId,
    Guid UserId,
    string UserName,
    string InstallationId,
    DevicePlatform Platform,
    string ProviderId,
    string MaskedAddress,
    bool IsEnabled,
    DeviceDisabledReason? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastSeenAt);

/// <summary>A registration an administrator may target when composing, with why it can or can't be used.</summary>
public sealed record DeliveryTargetOption(
    Guid RegistrationId,
    string InstallationId,
    DevicePlatform Platform,
    string ProviderId,
    string ProviderDisplayName,
    string MaskedAddress,
    bool IsCompatible,
    bool IsProviderConfigured,
    bool IsSimulated);
