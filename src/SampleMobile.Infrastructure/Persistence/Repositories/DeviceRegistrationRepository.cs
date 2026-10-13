using Microsoft.EntityFrameworkCore;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Persistence.Repositories;

internal sealed class DeviceRegistrationRepository(AppDbContext db) : IDeviceRegistrationRepository {
    public void Add(DeviceRegistration registration) => db.DeviceRegistrations.Add(registration);

    public Task<DeviceRegistration?> GetByIdAsync(Guid registrationId, CancellationToken cancellationToken = default) =>
        db.DeviceRegistrations.FirstOrDefaultAsync(registration => registration.Id == registrationId, cancellationToken);

    public async Task<IReadOnlyList<DeviceRegistration>> GetByIdsAsync(
        IReadOnlyCollection<Guid> registrationIds, CancellationToken cancellationToken = default) =>
        await db.DeviceRegistrations
            .Where(registration => registrationIds.Contains(registration.Id))
            .ToListAsync(cancellationToken);

    public Task<DeviceRegistration?> FindByInstallationAsync(
        string installationId, string providerId, CancellationToken cancellationToken = default) =>
        db.DeviceRegistrations.FirstOrDefaultAsync(
            registration => registration.InstallationId == installationId && registration.ProviderId == providerId,
            cancellationToken);

    public async Task<IReadOnlyList<DeviceRegistration>> ListEnabledWithAddressAsync(
        string providerId, string address, Guid excludeRegistrationId, CancellationToken cancellationToken = default) =>
        await db.DeviceRegistrations
            .Where(registration => registration.ProviderId == providerId
                && registration.Address == address
                && registration.IsEnabled
                && registration.Id != excludeRegistrationId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DeviceRegistration>> ListForUsersAsync(
        IReadOnlyCollection<Guid> userIds, bool enabledOnly, CancellationToken cancellationToken = default) =>
        await db.DeviceRegistrations
            .Where(registration => userIds.Contains(registration.UserId) && (!enabledOnly || registration.IsEnabled))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<DeviceRegistrationSummary>> ListAsync(
        DeviceRegistrationQuery query, CancellationToken cancellationToken = default) {
        var registrations =
            from registration in db.DeviceRegistrations.AsNoTracking()
            where query.IncludeDisabled || registration.IsEnabled
            join user in db.Users.AsNoTracking() on registration.UserId equals user.Id
            select new { Registration = registration, UserName = user.UserName };

        var totalCount = await registrations.CountAsync(cancellationToken);
        var items = await registrations
            .OrderByDescending(row => row.Registration.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(row => new DeviceRegistrationSummary(
                row.Registration.Id,
                row.Registration.UserId,
                row.UserName ?? string.Empty,
                row.Registration.InstallationId,
                row.Registration.Platform,
                row.Registration.ProviderId,
                row.Registration.Address,
                row.Registration.IsEnabled,
                row.Registration.DisabledReason,
                row.Registration.CreatedAt,
                row.Registration.UpdatedAt,
                row.Registration.LastSeenAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<DeviceRegistrationSummary>(items, query.Page, query.PageSize, totalCount);
    }
}
