using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Common;

namespace SampleMobile.Application.Features.Simulation;

public sealed record ListSimulatedMessagesQuery(int MaxCount = 50) : IQuery<OperationResult<IReadOnlyList<SimulatedPushRecord>>>;

public sealed class ListSimulatedMessagesQueryHandler(ISimulatedPushControl simulator)
    : IQueryHandler<ListSimulatedMessagesQuery, OperationResult<IReadOnlyList<SimulatedPushRecord>>> {

    public Task<OperationResult<IReadOnlyList<SimulatedPushRecord>>> HandleAsync(
        ListSimulatedMessagesQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<SimulatedPushRecord>>.Success(
            simulator.GetRecentMessages(Math.Clamp(query.MaxCount, 1, 200))));
}
