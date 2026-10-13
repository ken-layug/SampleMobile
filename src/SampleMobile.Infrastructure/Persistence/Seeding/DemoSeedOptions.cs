namespace SampleMobile.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo accounts for local development, bound from <c>DemoSeed</c>. Only ever applied when the host
/// environment is Development; the values live in <c>appsettings.Development.json</c> or user-secrets.
/// </summary>
public sealed class DemoSeedOptions {
    public const string SectionName = "DemoSeed";

    public bool Enabled { get; set; }

    /// <summary>Dashboard administrator.</summary>
    public DemoAccount? Admin { get; set; }

    /// <summary>Accounts the MAUI app (or curl) can sign in with.</summary>
    public List<DemoAccount> MobileUsers { get; set; } = [];

    /// <summary>User who receives a pre-registered virtual device on the simulated provider.</summary>
    public string? VirtualDeviceUserName { get; set; }
}

public sealed class DemoAccount {
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public sealed class DatabaseOptions {
    public const string SectionName = "Database";

    /// <summary>Applies pending EF Core migrations at startup. Convenient locally; prefer explicit migrations elsewhere.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}
