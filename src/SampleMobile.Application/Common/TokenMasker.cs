namespace SampleMobile.Application.Common;

/// <summary>
/// Masks provider addresses (FCM registration tokens and the like) for logs and admin screens.
/// A token is effectively a credential for pushing to a device, so it is never shown in full.
/// </summary>
public static class TokenMasker {
    private const int VisiblePrefix = 6;
    private const int VisibleSuffix = 4;

    public static string Mask(string? address) {
        if (string.IsNullOrEmpty(address)) {
            return string.Empty;
        }

        if (address.Length <= VisiblePrefix + VisibleSuffix + 2) {
            return new string('•', address.Length);
        }

        return $"{address[..VisiblePrefix]}…{address[^VisibleSuffix..]}";
    }
}
