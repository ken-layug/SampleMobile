using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Navigation;
using SampleMobile.Mobile.Core.Notifications;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Core.ViewModels;

namespace SampleMobile.Mobile.Core;

/// <summary>
/// Registers the provider-neutral client. The host adds the platform ports (<see cref="Platform"/> namespace)
/// and, optionally, real <see cref="IPushTransport"/> implementations; without one the app runs inbox-only.
/// </summary>
public static class DependencyInjection {
    public static IServiceCollection AddMobileCore(this IServiceCollection services, Action<MobileCoreOptions>? configure = null) {
        var options = new MobileCoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IBackendSettings, BackendSettings>();
        services.AddSingleton<ISessionStore, SessionStore>();

        services.AddTransient<BearerTokenHandler>();
        var http = services
            .AddHttpClient(MobileApiClient.HttpClientName, client => client.Timeout = options.RequestTimeout)
            .AddHttpMessageHandler<BearerTokenHandler>();
        if (options.CreatePrimaryHttpHandler is { } createHandler) {
            http.ConfigurePrimaryHttpMessageHandler(createHandler);
        }

        services.AddSingleton<MobileApiClient>();
        services.AddSingleton<IAuthApiClient>(sp => sp.GetRequiredService<MobileApiClient>());
        services.AddSingleton<IDeviceRegistrationApiClient>(sp => sp.GetRequiredService<MobileApiClient>());
        services.AddSingleton<INotificationApiClient>(sp => sp.GetRequiredService<MobileApiClient>());

        services.AddSingleton<InstallationIdProvider>();
        services.AddSingleton<InboxOnlyPushTransport>();
        services.AddSingleton<PushTransportSelector>();
        services.AddSingleton<RegistrationStateStore>();
        services.AddSingleton<DeviceRegistrationSync>();
        services.AddSingleton<INotificationInboxService, NotificationInboxService>();
        services.AddSingleton<NavigationCoordinator>();
        services.AddSingleton<PushEventProcessor>();
        services.AddSingleton<SessionBootstrapper>();
        services.AddSingleton<ISessionService, SessionService>();

        services.AddTransient<StartupViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<InboxViewModel>();
        services.AddTransient<NotificationDetailViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        return services;
    }
}
