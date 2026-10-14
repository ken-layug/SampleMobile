using Microsoft.Extensions.Logging;
using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Services;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Features.Devices;

/// <param name="UserId">Always the authenticated caller, resolved at the edge; never taken from the request body.</param>
public sealed record RegisterDeviceCommand(
    Guid UserId,
    string InstallationId,
    string Platform,
    string Provider,
    string Address) : ICommand<OperationResult<RegisterDeviceResult>>;

public sealed record RegisterDeviceResult(DeviceRegistrationResponse Registration, bool Created);

/// <summary>
/// Idempotent upsert keyed by (installation, provider). Repeating a registration refreshes it;
/// a new token replaces the old one; a different signed-in user takes the installation over;
/// and any other installation still holding the same token is disabled.
/// </summary>
public sealed class RegisterDeviceCommandHandler(
    IDeviceRegistrationRepository registrations,
    IPushProviderResolver providers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RegisterDeviceCommandHandler> logger) : ICommandHandler<RegisterDeviceCommand, OperationResult<RegisterDeviceResult>> {

    public async Task<OperationResult<RegisterDeviceResult>> HandleAsync(
        RegisterDeviceCommand command, CancellationToken cancellationToken = default) {
        var errors = new List<OperationError>();
        if (!DevicePlatforms.TryParse(command.Platform, out var platform)) {
            errors.Add(new OperationError("platform", "Platform must be 'android', 'ios' or 'virtual'."));
        }

        var providerId = DeviceRegistration.NormalizeProviderId(command.Provider);
        if (!providers.IsSupported(providerId)) {
            var supported = string.Join(", ", providers.DescribeAll().Select(provider => provider.ProviderId));
            errors.Add(new OperationError("provider", $"Provider '{providerId}' is not supported. Supported providers: {supported}."));
        }

        if (string.IsNullOrWhiteSpace(command.InstallationId)) {
            errors.Add(new OperationError("installationId", "Installation id is required."));
        }

        if (string.IsNullOrWhiteSpace(command.Address)) {
            errors.Add(new OperationError("address", "Address is required."));
        }

        if (errors.Count > 0) {
            return OperationResult<RegisterDeviceResult>.Invalid(errors);
        }

        var now = timeProvider.GetUtcNow();
        var installationId = command.InstallationId.Trim();
        var registration = await registrations.FindByInstallationAsync(installationId, providerId, cancellationToken);
        var created = registration is null;

        if (registration is null) {
            registration = DeviceRegistration.Register(command.UserId, installationId, platform, providerId, command.Address, now);
            registrations.Add(registration);
        } else if (registration.Refresh(command.UserId, platform, command.Address, now)) {
            logger.LogInformation(
                "Installation {InstallationId} ({ProviderId}) moved to user {UserId}",
                installationId, providerId, command.UserId);
        }

        // A provider token identifies one physical app instance. If another registration still holds it,
        // that registration is stale (reinstall, user switch) and would cause duplicate or misrouted pushes.
        var staleHolders = await registrations.ListEnabledWithAddressAsync(
            providerId, registration.Address, registration.Id, cancellationToken);
        foreach (var stale in staleHolders) {
            stale.Disable(DeviceDisabledReason.TokenReassigned, now);
        }

        try {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        } catch (UniqueConstraintViolationException) {
            return OperationResult<RegisterDeviceResult>.Conflict(
                "The installation was registered concurrently by another request. Retry the registration.");
        }

        logger.LogInformation(
            "{Action} device registration {RegistrationId} for user {UserId} ({Platform}, {ProviderId}, {Address})",
            created ? "Created" : "Refreshed",
            registration.Id,
            command.UserId,
            platform,
            providerId,
            TokenMasker.Mask(registration.Address));

        return OperationResult<RegisterDeviceResult>.Success(
            new RegisterDeviceResult(DeviceRegistrationResponse.FromEntity(registration), created));
    }
}
