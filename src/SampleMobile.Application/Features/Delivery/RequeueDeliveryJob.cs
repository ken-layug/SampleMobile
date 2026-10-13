using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;

namespace SampleMobile.Application.Features.Delivery;

/// <summary>
/// Puts a failed or cancelled job back in the queue with a fresh retry budget, e.g. after fixing
/// Firebase credentials. The worker still re-checks the registration before sending.
/// </summary>
public sealed record RequeueDeliveryJobCommand(Guid JobId) : ICommand<OperationResult>;

public sealed class RequeueDeliveryJobCommandHandler(
    IDeliveryJobRepository deliveryJobs,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<RequeueDeliveryJobCommand, OperationResult> {

    public async Task<OperationResult> HandleAsync(RequeueDeliveryJobCommand command, CancellationToken cancellationToken = default) {
        var job = await deliveryJobs.GetByIdAsync(command.JobId, cancellationToken);
        if (job is null) {
            return OperationResult.NotFound("Delivery job not found.");
        }

        if (!job.CanRequeue) {
            return OperationResult.Conflict($"Only failed or cancelled jobs can be requeued; this job is {job.Status}.");
        }

        job.Requeue(timeProvider.GetUtcNow());
        try {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        } catch (ConcurrencyConflictException) {
            return OperationResult.Conflict("The job changed while it was being requeued. Refresh and try again.");
        }

        return OperationResult.Success();
    }
}
