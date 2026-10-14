using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SampleMobile.Mobile.Core;
using SampleMobile.Mobile.Core.Platform;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Tests.Fakes;

/// <summary>
/// The real core service graph (via <c>AddMobileCore</c>) with in-memory platform ports, a stub backend at the
/// bottom of the real HTTP pipeline, an optional fake FCM transport and a controllable clock.
/// </summary>
public sealed class MobileTestHost : IDisposable {
    public static readonly DateTimeOffset Start = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    public const string BackendUrl = "http://backend.test/";

    private readonly ServiceProvider _services;
    private int _tokenCounter;

    public MobileTestHost(bool withPushTransport = true) {
        Permissions.GetStatusAsync().Returns(NotificationPermissionState.Granted);
        Permissions.RequestIfNeededAsync().Returns(NotificationPermissionState.Granted);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddMobileCore(options => {
            options.DefaultBackendUrl = BackendUrl;
            options.CreatePrimaryHttpHandler = () => Http;
            options.RegistrationRetryDelays = [TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1)];
        });
        services.AddSingleton<ISecureStore>(SecureStore);
        services.AddSingleton<IAppPreferences>(Preferences);
        services.AddSingleton<IMainThreadDispatcher, InlineDispatcher>();
        services.AddSingleton(Permissions);
        services.AddSingleton(Presenter);
        services.AddSingleton(Navigation);
        if (withPushTransport) {
            services.AddSingleton<IPushTransport>(Fcm);
        }

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Get<PushEventProcessor>().Start();
        UseHealthyBackend();
    }

    public StubHttpHandler Http { get; } = new();

    public FakeTimeProvider Time { get; } = new(Start);

    public InMemorySecureStore SecureStore { get; } = new();

    public InMemoryPreferences Preferences { get; } = new();

    public FakePushTransport Fcm { get; } = new();

    public INotificationPermissionService Permissions { get; } = Substitute.For<INotificationPermissionService>();

    public ILocalNotificationPresenter Presenter { get; } = Substitute.For<ILocalNotificationPresenter>();

    public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Signs in through the session service and waits for the start-up pipeline (registration, inbox) to finish.</summary>
    public async Task SignInAsync(string userName = "alice") {
        var result = await Get<ISessionService>().LoginAsync(userName, "Pa$$w0rd!", TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Error);
        await Get<SessionBootstrapper>().RunAsync(BootstrapReason.Login, TestContext.Current.CancellationToken);
    }

    /// <summary>A backend that accepts everything: login, registration, an empty inbox, unregister.</summary>
    public void UseHealthyBackend() {
        Http.On(HttpMethod.Post, "/api/mobile/auth/login", request => {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var user = body.Contains("\"bob\"", StringComparison.Ordinal) ? "bob" : "alice";
            return StubHttpHandler.Json(HttpStatusCode.OK, BackendJson.Login($"token-{user}-{Interlocked.Increment(ref _tokenCounter)}", Start.AddHours(8)));
        });
        Http.On(HttpMethod.Post, "/api/mobile/device-registrations", HttpStatusCode.Created, BackendJson.Registration());
        Http.On(HttpMethod.Get, "/api/mobile/notifications", HttpStatusCode.OK, BackendJson.List());
        Http.On(HttpMethod.Delete, $"/api/mobile/device-registrations/{InstallationId}", HttpStatusCode.NoContent);
    }

    public string InstallationId => Get<InstallationIdProvider>().GetOrCreate();

    public IReadOnlyList<RecordedRequest> Registrations => Http.RequestsTo(HttpMethod.Post, "/api/mobile/device-registrations");

    public void Dispose() => _services.Dispose();
}
