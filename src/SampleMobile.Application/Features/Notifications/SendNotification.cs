using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Common;
using SampleMobile.Application.Services;

namespace SampleMobile.Application.Features.Notifications;

/// <summary>
/// An administrator composes a notification directly — no domain event involved.
/// </summary>
/// <param name="TargetRegistrationIds">The registrations to push to; empty for inbox-only, <c>null</c> for all compatible ones.</param>
public sealed record SendNotificationCommand(
    Guid RecipientUserId,
    string Type,
    string Title,
    string Body,
    string? ResourceId,
    IReadOnlyDictionary<string, string>? Metadata,
    IReadOnlyCollection<Guid>? TargetRegistrationIds) : ICommand<OperationResult<PublishedNotification>>;

public sealed class SendNotificationCommandHandler(INotificationPublisher publisher)
    : ICommandHandler<SendNotificationCommand, OperationResult<PublishedNotification>> {

    public Task<OperationResult<PublishedNotification>> HandleAsync(
        SendNotificationCommand command, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(
            new NotificationIntent(
                command.Type,
                command.Title,
                command.Body,
                [command.RecipientUserId],
                command.Metadata,
                string.IsNullOrWhiteSpace(command.ResourceId) ? null : command.ResourceId.Trim(),
                DeduplicationKey: null,
                command.TargetRegistrationIds),
            cancellationToken);
}
