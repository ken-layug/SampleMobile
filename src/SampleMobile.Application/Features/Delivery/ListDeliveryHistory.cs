using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Features.Delivery;

public sealed record ListDeliveryHistoryQuery(
    int Page,
    int PageSize,
    DeliveryJobStatus? Status = null,
    string? ProviderId = null,
    Guid? NotificationId = null,
    bool SimulatedOnly = false) : IQuery<OperationResult<PagedResult<DeliveryJobSummary>>>;

public sealed class ListDeliveryHistoryQueryHandler(IDeliveryJobRepository deliveryJobs)
    : IQueryHandler<ListDeliveryHistoryQuery, OperationResult<PagedResult<DeliveryJobSummary>>> {

    public async Task<OperationResult<PagedResult<DeliveryJobSummary>>> HandleAsync(
        ListDeliveryHistoryQuery query, CancellationToken cancellationToken = default) {
        var errors = Paging.Validate(query.Page, query.PageSize);
        if (errors.Count > 0) {
            return OperationResult<PagedResult<DeliveryJobSummary>>.Invalid(errors);
        }

        var providerId = string.IsNullOrWhiteSpace(query.ProviderId)
            ? null
            : DeviceRegistration.NormalizeProviderId(query.ProviderId);

        var page = await deliveryJobs.ListHistoryAsync(
            new DeliveryHistoryQuery(query.Page, query.PageSize, query.Status, providerId, query.NotificationId, query.SimulatedOnly),
            cancellationToken);

        return OperationResult<PagedResult<DeliveryJobSummary>>.Success(page);
    }
}
