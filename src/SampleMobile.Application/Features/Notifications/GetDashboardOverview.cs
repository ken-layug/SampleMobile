using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Services;

namespace SampleMobile.Application.Features.Notifications;

public sealed record GetDashboardOverviewQuery : IQuery<OperationResult<DashboardOverview>>;

public sealed class GetDashboardOverviewQueryHandler(
    IDashboardStatistics statistics,
    IPushProviderResolver providers) : IQueryHandler<GetDashboardOverviewQuery, OperationResult<DashboardOverview>> {

    public async Task<OperationResult<DashboardOverview>> HandleAsync(
        GetDashboardOverviewQuery query, CancellationToken cancellationToken = default) {
        var stats = await statistics.GetAsync(cancellationToken);
        return OperationResult<DashboardOverview>.Success(new DashboardOverview(stats, providers.DescribeAll()));
    }
}
