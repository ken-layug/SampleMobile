using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SampleMobile.Infrastructure.Authentication;

/// <summary>
/// Resolves the one signing key both token issuance and token validation use. Outside Development a
/// missing or short key stops the app at startup; in Development an ephemeral key is generated so the
/// app runs with zero setup, at the cost of tokens becoming invalid when the process restarts.
/// </summary>
public sealed class JwtSigningKeyProvider {
    public JwtSigningKeyProvider(IOptions<JwtOptions> options, IHostEnvironment environment, ILogger<JwtSigningKeyProvider> logger) {
        var configuredKey = options.Value.SigningKey;
        if (!string.IsNullOrWhiteSpace(configuredKey)) {
            var bytes = Encoding.UTF8.GetBytes(configuredKey);
            if (bytes.Length < JwtOptions.MinimumKeyBytes) {
                throw new InvalidOperationException(
                    $"Jwt:SigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes long.");
            }

            Key = new SymmetricSecurityKey(bytes);
            return;
        }

        if (!environment.IsDevelopment()) {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it with user-secrets or the Jwt__SigningKey environment variable.");
        }

        logger.LogWarning(
            "Jwt:SigningKey is not configured; using an ephemeral development key. Mobile tokens become invalid " +
            "when the app restarts. Set a stable key with: dotnet user-secrets set \"Jwt:SigningKey\" \"<32+ random characters>\"");
        Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
    }

    public SymmetricSecurityKey Key { get; }
}
