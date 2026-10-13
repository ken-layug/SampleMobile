using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SampleMobile.Application.Abstractions.Identity;

namespace SampleMobile.Infrastructure.Authentication;

internal sealed class JwtAccessTokenIssuer(
    JwtSigningKeyProvider keyProvider,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IAccessTokenIssuer {

    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(AuthenticatedUser user) {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + settings.AccessTokenLifetime;

        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(user.Roles.Select(role => new Claim("role", role)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(keyProvider.Key, SecurityAlgorithms.HmacSha256)
        });

        return new AccessToken(token, expiresAt);
    }
}
