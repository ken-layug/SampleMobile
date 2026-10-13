using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Abstractions.Events;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Infrastructure.Authentication;
using SampleMobile.Infrastructure.BackgroundServices;
using SampleMobile.Infrastructure.Events;
using SampleMobile.Infrastructure.Identity;
using SampleMobile.Infrastructure.Persistence;
using SampleMobile.Infrastructure.Persistence.Interceptors;
using SampleMobile.Infrastructure.Persistence.Repositories;
using SampleMobile.Infrastructure.Persistence.Seeding;
using SampleMobile.Infrastructure.Push.Simulated;

namespace SampleMobile.Infrastructure;

public static class DependencyInjection {
    public const string ConnectionStringName = "SampleMobile";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder) {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<DeliveryOptions>().Bind(configuration.GetSection(DeliveryOptions.SectionName));
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<DemoSeedOptions>().Bind(configuration.GetSection(DemoSeedOptions.SectionName));
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddOptions<SimulatedProviderOptions>().Bind(configuration.GetSection(SimulatedProviderOptions.SectionName));

        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));

        AddPersistence(services, configuration);
        AddIdentity(services);
        AddPushProviders(services);
        AddBackgroundProcessing(services);

        return builder;
    }

    private static void AddBackgroundProcessing(IServiceCollection services) {
        services.AddScoped<IDomainEventDispatcher, OutboxDomainEventDispatcher>();

        // All three run in the web process for the POC. Each can be switched off by configuration,
        // and each is safe to run in several processes at once (SKIP LOCKED claims, LISTEN per process).
        services.AddHostedService<PostgresWorkListener>();
        services.AddHostedService<OutboxProcessor>();
        services.AddHostedService<DeliveryWorker>();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration) {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString)) {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured. See README.md for the local PostgreSQL setup.");
        }

        // One data source for EF and for the LISTEN connection, so both share pooling and configuration.
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<WorkSignal>();

        // Scoped, like the DbContext options: it carries state between SavingChanges and SavedChanges.
        services.AddScoped<WorkAvailableInterceptor>();
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<WorkAvailableInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDeviceRegistrationRepository, DeviceRegistrationRepository>();
        services.AddScoped<IDeliveryJobRepository, DeliveryJobRepository>();
        services.AddScoped<IDemoAlertRepository, DemoAlertRepository>();
        services.AddScoped<IDashboardStatistics, DashboardStatistics>();
    }

    private static void AddIdentity(IServiceCollection services) {
        services.AddIdentityCore<AppUser>(options => {
                options.User.RequireUniqueEmail = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        services.AddScoped<IIdentityService, IdentityService>();

        // Singleton so issuing and validating tokens use the same key, including the ephemeral development key.
        services.AddSingleton<JwtSigningKeyProvider>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
    }

    private static void AddPushProviders(IServiceCollection services) {
        // Each adapter is registered once as IPushNotificationProvider; the resolver indexes them by ProviderId.
        // Adding a provider means adding an adapter and one line here — no use case changes.
        services.AddSingleton<SimulatedPushControl>();
        services.AddSingleton<ISimulatedPushControl>(provider => provider.GetRequiredService<SimulatedPushControl>());
        services.AddSingleton<IPushNotificationProvider, SimulatedPushNotificationProvider>();
    }
}
