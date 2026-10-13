using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Identity;
using SampleMobile.Infrastructure.Push;

namespace SampleMobile.Infrastructure.Persistence.Seeding;

/// <summary>
/// Applies migrations and seeds demo data at startup. Seeding goes through <see cref="UserManager{TUser}"/>
/// rather than EF's UseSeeding because Identity users need password hashing, normalization and security stamps.
/// Every step is check-then-insert, so running it on every start is safe.
/// </summary>
public static class DatabaseInitializer {
    public const string SeededVirtualInstallationId = "seed-virtual-device";

    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default) {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var db = provider.GetRequiredService<AppDbContext>();

        if (provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup) {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(cancellationToken);
        }

        var seed = provider.GetRequiredService<IOptions<DemoSeedOptions>>().Value;
        if (!seed.Enabled) {
            return;
        }

        if (!provider.GetRequiredService<IHostEnvironment>().IsDevelopment()) {
            logger.LogWarning("DemoSeed:Enabled is set outside Development; demo accounts are never created in other environments");
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync(Roles.Admin)) {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin) { Id = Guid.CreateVersion7() }), "create the Admin role");
        }

        if (seed.Admin is { } admin) {
            var adminUser = await EnsureUserAsync(userManager, admin, logger);
            if (!await userManager.IsInRoleAsync(adminUser, Roles.Admin)) {
                EnsureSucceeded(await userManager.AddToRoleAsync(adminUser, Roles.Admin), "grant the Admin role");
            }
        }

        foreach (var account in seed.MobileUsers) {
            await EnsureUserAsync(userManager, account, logger);
        }

        if (!string.IsNullOrWhiteSpace(seed.VirtualDeviceUserName)
            && await userManager.FindByNameAsync(seed.VirtualDeviceUserName) is { } deviceOwner) {
            await EnsureVirtualDeviceAsync(db, deviceOwner, provider.GetRequiredService<TimeProvider>(), cancellationToken);
        }
    }

    private static async Task<AppUser> EnsureUserAsync(UserManager<AppUser> userManager, DemoAccount account, ILogger logger) {
        if (string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.Password)) {
            throw new InvalidOperationException("Demo accounts need both a user name and a password.");
        }

        var existing = await userManager.FindByNameAsync(account.UserName);
        if (existing is not null) {
            return existing;
        }

        var user = new AppUser { UserName = account.UserName, DisplayName = account.DisplayName };
        EnsureSucceeded(await userManager.CreateAsync(user, account.Password), $"create demo user '{account.UserName}'");
        logger.LogInformation("Seeded demo user {UserName}", account.UserName);
        return user;
    }

    private static async Task EnsureVirtualDeviceAsync(
        AppDbContext db, AppUser owner, TimeProvider timeProvider, CancellationToken cancellationToken) {
        var exists = await db.DeviceRegistrations.AnyAsync(
            registration => registration.InstallationId == SeededVirtualInstallationId
                && registration.ProviderId == PushProviderIds.Simulated,
            cancellationToken);
        if (exists) {
            return;
        }

        db.DeviceRegistrations.Add(DeviceRegistration.Register(
            owner.Id,
            SeededVirtualInstallationId,
            DevicePlatform.Virtual,
            PushProviderIds.Simulated,
            $"sim-seeded-{owner.UserName}-virtual-device",
            timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string action) {
        if (!result.Succeeded) {
            throw new InvalidOperationException(
                $"Could not {action}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
    }
}
