namespace SampleMobile.Mobile.Core.Push;

/// <summary>
/// Masks provider addresses for logs and diagnostics. Same rule as the backend's <c>TokenMasker</c>:
/// a token is a credential for pushing to this device, so it is never shown in full.
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
