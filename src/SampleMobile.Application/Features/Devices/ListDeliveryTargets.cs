using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Services;

namespace SampleMobile.Application.Features.Devices;

/// <summary>
/// The enabled registrations an administrator can target for one user. Compatibility is a property
/// of each registration's own provider; the admin chooses among transports, never re-routes a token.
/// </summary>
public sealed record ListDeliveryTargetsQuery(Guid UserId) : IQuery<OperationResult<IReadOnlyList<DeliveryTargetOption>>>;

public sealed class ListDeliveryTargetsQueryHandler(
    IDeviceRegistrationRepository registrations,
    IPushProviderResolver providers) : IQueryHandler<ListDeliveryTargetsQuery, OperationResult<IReadOnlyList<DeliveryTargetOption>>> {

    public async Task<OperationResult<IReadOnlyList<DeliveryTargetOption>>> HandleAsync(
        ListDeliveryTargetsQuery query, CancellationToken cancellationToken = default) {
        var enabled = await registrations.ListForUsersAsync([query.UserId], enabledOnly: true, cancellationToken);

        IReadOnlyList<DeliveryTargetOption> options = enabled
            .OrderByDescending(registration => registration.LastSeenAt)
            .Select(registration => {
                var descriptor = providers.TryResolve(registration.ProviderId, out var provider) ? provider.Describe() : null;
                return new DeliveryTargetOption(
                    registration.Id,
                    registration.InstallationId,
                    registration.Platform,
                    registration.ProviderId,
                    descriptor?.DisplayName ?? $"{registration.ProviderId} (no adapter registered)",
                    TokenMasker.Mask(registration.Address),
                    IsCompatible: descriptor is not null,
                    IsProviderConfigured: descriptor?.IsConfigured ?? false,
                    IsSimulated: descriptor?.IsSimulated ?? false);
            })
            .ToList();

        return OperationResult<IReadOnlyList<DeliveryTargetOption>>.Success(options);
    }
}
