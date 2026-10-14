using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Application.Features.Devices;

public sealed record ListMyDevicesQuery(Guid UserId) : IQuery<OperationResult<IReadOnlyList<DeviceRegistrationResponse>>>;

public sealed class ListMyDevicesQueryHandler(IDeviceRegistrationRepository registrations)
    : IQueryHandler<ListMyDevicesQuery, OperationResult<IReadOnlyList<DeviceRegistrationResponse>>> {

    public async Task<OperationResult<IReadOnlyList<DeviceRegistrationResponse>>> HandleAsync(
        ListMyDevicesQuery query, CancellationToken cancellationToken = default) {
        var owned = await registrations.ListForUsersAsync([query.UserId], enabledOnly: false, cancellationToken);
        IReadOnlyList<DeviceRegistrationResponse> response = owned
            .OrderByDescending(registration => registration.UpdatedAt)
            .Select(DeviceRegistrationResponse.FromEntity)
            .ToList();
        return OperationResult<IReadOnlyList<DeviceRegistrationResponse>>.Success(response);
    }
}
