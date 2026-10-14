namespace SampleMobile.Infrastructure.Authentication;

/// <summary>Mobile API token settings, bound from the <c>Jwt</c> section.</summary>
public sealed class JwtOptions {
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "SampleMobile";
    public string Audience { get; set; } = "SampleMobile.MobileApi";
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// HMAC signing key (at least 32 bytes). Supply it through user-secrets or the <c>Jwt__SigningKey</c>
    /// environment variable; never commit it. Development falls back to a per-process random key.
    /// </summary>
    public string? SigningKey { get; set; }
}
