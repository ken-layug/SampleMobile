using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Features.DemoAlerts;

public sealed record RaiseDemoAlertCommand(Guid RecipientUserId, DemoAlertSeverity Severity, string Message)
    : ICommand<OperationResult<RaisedDemoAlert>>;

public sealed record RaisedDemoAlert(Guid AlertId, Guid EventId);

/// <summary>
/// Stores a piece of sample business data. Its <c>DemoAlertRaised</c> event lands in the outbox in the
/// same transaction; this handler does not create a notification and never talks to a push provider.
/// </summary>
public sealed class RaiseDemoAlertCommandHandler(
    IDemoAlertRepository alerts,
    IIdentityService identity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<RaiseDemoAlertCommand, OperationResult<RaisedDemoAlert>> {

    public async Task<OperationResult<RaisedDemoAlert>> HandleAsync(
        RaiseDemoAlertCommand command, CancellationToken cancellationToken = default) {
        var errors = new List<OperationError>();
        if (string.IsNullOrWhiteSpace(command.Message)) {
            errors.Add(new OperationError("message", "Message is required."));
        } else if (command.Message.Trim().Length > DemoAlert.MaxMessageLength) {
            errors.Add(new OperationError("message", $"Message must be at most {DemoAlert.MaxMessageLength} characters."));
        }

        if (await identity.FindByIdAsync(command.RecipientUserId, cancellationToken) is null) {
            errors.Add(new OperationError("recipientUserId", "The recipient does not exist."));
        }

        if (errors.Count > 0) {
            return OperationResult<RaisedDemoAlert>.Invalid(errors);
        }

        var alert = DemoAlert.Create(command.RecipientUserId, command.Severity, command.Message, timeProvider.GetUtcNow());
        var eventId = alert.DomainEvents.Single().EventId;
        alerts.Add(alert);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationResult<RaisedDemoAlert>.Success(new RaisedDemoAlert(alert.Id, eventId));
    }
}
