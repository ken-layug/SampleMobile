using System.Diagnostics.CodeAnalysis;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Services;

/// <summary>Finds the push provider for a device registration's provider id.</summary>
public interface IPushProviderResolver {
    /// <summary>
    /// Resolves the provider registered under exactly <paramref name="providerId"/>. There is no
    /// fallback: an FCM token is never handed to the simulator, APNs, or anything else.
    /// </summary>
    bool TryResolve(string providerId, [NotNullWhen(true)] out IPushNotificationProvider? provider);

    bool IsSupported(string providerId);

    IReadOnlyList<PushProviderDescriptor> DescribeAll();
}

public sealed class PushProviderResolver : IPushProviderResolver {
    private readonly Dictionary<string, IPushNotificationProvider> _providers;

    public PushProviderResolver(IEnumerable<IPushNotificationProvider> providers) {
        _providers = new Dictionary<string, IPushNotificationProvider>(StringComparer.Ordinal);
        foreach (var provider in providers) {
            var id = DeviceRegistration.NormalizeProviderId(provider.ProviderId);
            if (!_providers.TryAdd(id, provider)) {
                throw new InvalidOperationException($"More than one push provider is registered with the id '{id}'.");
            }
        }
    }

    public bool TryResolve(string providerId, [NotNullWhen(true)] out IPushNotificationProvider? provider) {
        provider = null;
        return !string.IsNullOrWhiteSpace(providerId)
            && _providers.TryGetValue(DeviceRegistration.NormalizeProviderId(providerId), out provider);
    }

    public bool IsSupported(string providerId) => TryResolve(providerId, out _);

    public IReadOnlyList<PushProviderDescriptor> DescribeAll() =>
        _providers.Values
            .Select(provider => provider.Describe())
            .OrderBy(descriptor => descriptor.IsSimulated)
            .ThenBy(descriptor => descriptor.ProviderId, StringComparer.Ordinal)
            .ToList();
}
