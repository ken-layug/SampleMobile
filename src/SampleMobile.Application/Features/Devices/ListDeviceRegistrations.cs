using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Application.Features.Devices;

/// <summary>Admin view of every registration. Addresses are masked here, before anything leaves the use case.</summary>
public sealed record ListDeviceRegistrationsQuery(int Page, int PageSize, bool IncludeDisabled)
    : IQuery<OperationResult<PagedResult<AdminDeviceRegistrationItem>>>;

public sealed class ListDeviceRegistrationsQueryHandler(IDeviceRegistrationRepository registrations)
    : IQueryHandler<ListDeviceRegistrationsQuery, OperationResult<PagedResult<AdminDeviceRegistrationItem>>> {

    public async Task<OperationResult<PagedResult<AdminDeviceRegistrationItem>>> HandleAsync(
        ListDeviceRegistrationsQuery query, CancellationToken cancellationToken = default) {
        var errors = Paging.Validate(query.Page, query.PageSize);
        if (errors.Count > 0) {
            return OperationResult<PagedResult<AdminDeviceRegistrationItem>>.Invalid(errors);
        }

        var page = await registrations.ListAsync(
            new DeviceRegistrationQuery(query.Page, query.PageSize, query.IncludeDisabled), cancellationToken);

        var items = page.Items
            .Select(item => new AdminDeviceRegistrationItem(
                item.RegistrationId,
                item.UserId,
                item.UserName,
                item.InstallationId,
                item.Platform,
                item.ProviderId,
                TokenMasker.Mask(item.Address),
                item.IsEnabled,
                item.DisabledReason,
                item.CreatedAt,
                item.UpdatedAt,
                item.LastSeenAt))
            .ToList();

        return OperationResult<PagedResult<AdminDeviceRegistrationItem>>.Success(
            new PagedResult<AdminDeviceRegistrationItem>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
