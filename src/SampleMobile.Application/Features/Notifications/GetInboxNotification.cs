using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Application.Features.Notifications;

public sealed record GetInboxNotificationQuery(Guid UserId, Guid NotificationId)
    : IQuery<OperationResult<NotificationResponse>>;

public sealed class GetInboxNotificationQueryHandler(INotificationRepository notifications)
    : IQueryHandler<GetInboxNotificationQuery, OperationResult<NotificationResponse>> {

    public async Task<OperationResult<NotificationResponse>> HandleAsync(
        GetInboxNotificationQuery query, CancellationToken cancellationToken = default) {
        var item = await notifications.GetInboxItemAsync(query.UserId, query.NotificationId, cancellationToken);

        // A notification addressed to someone else is indistinguishable from one that doesn't exist.
        return item is null
            ? OperationResult<NotificationResponse>.NotFound("Notification not found.")
            : OperationResult<NotificationResponse>.Success(NotificationResponse.FromInboxItem(item));
    }
}
