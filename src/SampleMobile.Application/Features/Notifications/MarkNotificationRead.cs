using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Application.Features.Notifications;

public sealed record MarkNotificationReadCommand(Guid UserId, Guid NotificationId)
    : ICommand<OperationResult<NotificationResponse>>;

/// <summary>Idempotent: marking an already-read notification returns the original read time.</summary>
public sealed class MarkNotificationReadCommandHandler(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<MarkNotificationReadCommand, OperationResult<NotificationResponse>> {

    public async Task<OperationResult<NotificationResponse>> HandleAsync(
        MarkNotificationReadCommand command, CancellationToken cancellationToken = default) {
        var notification = await notifications.GetByIdAsync(command.NotificationId, cancellationToken);
        if (notification?.FindRecipient(command.UserId) is null) {
            return OperationResult<NotificationResponse>.NotFound("Notification not found.");
        }

        var recipient = notification.MarkReadBy(command.UserId, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationResult<NotificationResponse>.Success(new NotificationResponse(
            notification.Id,
            notification.Type.Value,
            notification.Title,
            notification.Body,
            notification.CreatedAt,
            recipient.ReadAt,
            notification.ResourceId,
            notification.Metadata));
    }
}
