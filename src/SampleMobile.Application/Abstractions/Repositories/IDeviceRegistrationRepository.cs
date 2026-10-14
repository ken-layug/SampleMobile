using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Abstractions.Repositories;

public interface IDeviceRegistrationRepository {
    void Add(DeviceRegistration registration);

    Task<DeviceRegistration?> GetByIdAsync(Guid registrationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceRegistration>> GetByIdsAsync(
        IReadOnlyCollection<Guid> registrationIds, CancellationToken cancellationToken = default);

    /// <summary>Finds the registration for an installation on one provider, whoever owns it.</summary>
    Task<DeviceRegistration?> FindByInstallationAsync(
        string installationId, string providerId, CancellationToken cancellationToken = default);

    /// <summary>Other enabled registrations that hold the same provider address (the token moved to another installation).</summary>
    Task<IReadOnlyList<DeviceRegistration>> ListEnabledWithAddressAsync(
        string providerId, string address, Guid excludeRegistrationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceRegistration>> ListForUsersAsync(
        IReadOnlyCollection<Guid> userIds, bool enabledOnly, CancellationToken cancellationToken = default);

    /// <summary>Admin listing. Includes the raw address; callers must mask it before display.</summary>
    Task<PagedResult<DeviceRegistrationSummary>> ListAsync(
        DeviceRegistrationQuery query, CancellationToken cancellationToken = default);
}

public sealed record DeviceRegistrationQuery(int Page, int PageSize, bool IncludeDisabled);

public sealed record DeviceRegistrationSummary(
    Guid RegistrationId,
    Guid UserId,
    string UserName,
    string InstallationId,
    DevicePlatform Platform,
    string ProviderId,
    string Address,
    bool IsEnabled,
    DeviceDisabledReason? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastSeenAt);
