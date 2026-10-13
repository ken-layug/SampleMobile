using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace SampleMobile.Web.Common;

public static class ClaimsPrincipalExtensions {
    /// <summary>
    /// The authenticated user's id: <c>sub</c> for mobile bearer tokens, the name identifier for the
    /// dashboard cookie. Only call this behind an authorization policy, which guarantees the claim exists.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException("The authenticated principal carries no user id claim.");
    }
}
