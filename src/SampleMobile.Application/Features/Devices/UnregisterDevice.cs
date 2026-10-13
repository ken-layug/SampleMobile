using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Features.Devices;

/// <param name="UserId">The authenticated caller; only their own registration can be removed.</param>
public sealed record UnregisterDeviceCommand(Guid UserId, string InstallationId, string Provider) : ICommand<OperationResult>;

/// <summary>
/// Disables rather than deletes, so delivery history keeps pointing at a real row.
/// Repeating the call is harmless; registering again re-enables the installation.
/// </summary>
public sealed class UnregisterDeviceCommandHandler(
    IDeviceRegistrationRepository registrations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<UnregisterDeviceCommand, OperationResult> {

    public async Task<OperationResult> HandleAsync(UnregisterDeviceCommand command, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(command.Provider)) {
            return OperationResult.Invalid("provider", "The provider query parameter is required.");
        }

        var registration = await registrations.FindByInstallationAsync(
            command.InstallationId.Trim(), DeviceRegistration.NormalizeProviderId(command.Provider), cancellationToken);

        // Another user's registration is reported exactly like a missing one, so ids can't be probed.
        if (registration is null || registration.UserId != command.UserId) {
            return OperationResult.NotFound("No registration exists for this installation and provider.");
        }

        registration.Disable(DeviceDisabledReason.Unregistered, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OperationResult.Success();
    }
}
