using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Common;

namespace SampleMobile.Application.Features.Users;

/// <summary>Users an administrator can address or inspect in the dashboard.</summary>
public sealed record ListDemoUsersQuery : IQuery<OperationResult<IReadOnlyList<UserSummary>>>;

public sealed class ListDemoUsersQueryHandler(IIdentityService identity)
    : IQueryHandler<ListDemoUsersQuery, OperationResult<IReadOnlyList<UserSummary>>> {

    public async Task<OperationResult<IReadOnlyList<UserSummary>>> HandleAsync(
        ListDemoUsersQuery query, CancellationToken cancellationToken = default) =>
        OperationResult<IReadOnlyList<UserSummary>>.Success(await identity.ListUsersAsync(cancellationToken));
}
