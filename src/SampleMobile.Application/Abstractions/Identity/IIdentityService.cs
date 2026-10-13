namespace SampleMobile.Application.Abstractions.Identity;

/// <summary>
/// Access to user accounts. ASP.NET Core Identity's <c>UserManager</c> never appears above Infrastructure;
/// use cases see users only through this port.
/// </summary>
public interface IIdentityService {
    /// <summary>Checks a password (with lockout). Returns <c>null</c> for unknown users, wrong passwords and locked accounts alike.</summary>
    Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string userName, string password, CancellationToken cancellationToken = default);

    Task<UserSummary?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken = default);
}

public sealed record AuthenticatedUser(Guid Id, string UserName, IReadOnlyList<string> Roles);

public sealed record UserSummary(Guid Id, string UserName, string? DisplayName, IReadOnlyList<string> Roles) {
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? UserName : $"{DisplayName} ({UserName})";
}

/// <summary>Issues bearer tokens for the mobile API.</summary>
public interface IAccessTokenIssuer {
    AccessToken Issue(AuthenticatedUser user);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
