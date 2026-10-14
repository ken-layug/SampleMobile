using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Application.Features.Notifications;

/// <param name="UserId">The inbox owner: the authenticated caller, or a user an administrator is inspecting.</param>
public sealed record GetInboxQuery(Guid UserId, int Page, int PageSize, bool UnreadOnly)
    : IQuery<OperationResult<NotificationListResponse>>;

public sealed class GetInboxQueryHandler(INotificationRepository notifications)
    : IQueryHandler<GetInboxQuery, OperationResult<NotificationListResponse>> {

    public async Task<OperationResult<NotificationListResponse>> HandleAsync(
        GetInboxQuery query, CancellationToken cancellationToken = default) {
        var errors = Paging.Validate(query.Page, query.PageSize);
        if (errors.Count > 0) {
            return OperationResult<NotificationListResponse>.Invalid(errors);
        }

        var page = await notifications.GetInboxAsync(
            new InboxQuery(query.UserId, query.Page, query.PageSize, query.UnreadOnly), cancellationToken);
        var unreadCount = await notifications.CountUnreadAsync(query.UserId, cancellationToken);

        return OperationResult<NotificationListResponse>.Success(new NotificationListResponse(
            page.Items.Select(NotificationResponse.FromInboxItem).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount,
            unreadCount));
    }
}
