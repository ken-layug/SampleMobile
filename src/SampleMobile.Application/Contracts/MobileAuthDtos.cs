using System.ComponentModel.DataAnnotations;

namespace SampleMobile.Application.Contracts;

/// <summary>Credentials for signing in to the mobile API.</summary>
public sealed record LoginRequest {
    /// <summary>The account's user name.</summary>
    [Required]
    [MaxLength(256)]
    public string? UserName { get; init; }

    /// <summary>The account's password.</summary>
    [Required]
    [MaxLength(256)]
    public string? Password { get; init; }
}

/// <summary>A bearer token for the mobile API. Send it as <c>Authorization: Bearer {accessToken}</c>.</summary>
/// <param name="AccessToken">The signed access token.</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="ExpiresAt">When the token stops being accepted (UTC).</param>
/// <param name="ExpiresIn">Seconds until <paramref name="ExpiresAt"/>, for clients without a reliable clock.</param>
public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, int ExpiresIn);
