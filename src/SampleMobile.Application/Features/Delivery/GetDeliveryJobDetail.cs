using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;

namespace SampleMobile.Application.Features.Delivery;

public sealed record GetDeliveryJobDetailQuery(Guid JobId) : IQuery<OperationResult<DeliveryJobDetail>>;

public sealed class GetDeliveryJobDetailQueryHandler(IDeliveryJobRepository deliveryJobs)
    : IQueryHandler<GetDeliveryJobDetailQuery, OperationResult<DeliveryJobDetail>> {

    public async Task<OperationResult<DeliveryJobDetail>> HandleAsync(
        GetDeliveryJobDetailQuery query, CancellationToken cancellationToken = default) {
        var detail = await deliveryJobs.GetDetailAsync(query.JobId, cancellationToken);
        return detail is null
            ? OperationResult<DeliveryJobDetail>.NotFound("Delivery job not found.")
            : OperationResult<DeliveryJobDetail>.Success(detail);
    }
}
