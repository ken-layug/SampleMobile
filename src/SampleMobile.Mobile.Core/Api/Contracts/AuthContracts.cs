namespace SampleMobile.Mobile.Core.Api.Contracts;

/// <summary>Body of <c>POST /api/mobile/auth/login</c>.</summary>
public sealed record LoginRequest(string UserName, string Password);

/// <summary>Bearer token issued by the backend. The token is opaque to the app; there is no refresh token.</summary>
public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, int ExpiresIn);
