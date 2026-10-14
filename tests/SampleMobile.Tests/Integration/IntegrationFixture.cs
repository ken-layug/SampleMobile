using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SampleMobile.Application.Contracts;
using Testcontainers.PostgreSql;

namespace SampleMobile.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture> {
    public const string Name = "PostgreSQL integration";
}

/// <summary>
/// One real PostgreSQL container and one in-memory app host for the whole integration collection.
/// The app migrates and seeds the database itself on startup, exactly as in local development.
/// Background workers are switched off so tests drive claiming and dispatch deterministically.
/// Requires Docker.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime {
    public const string AliceUserName = "it-alice";
    public const string BobUserName = "it-bob";

    // Test-only accounts that exist solely in the throwaway container.
    private const string AlicePassword = "It#Alice2026";
    private const string BobPassword = "It#Bob2026";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public SampleMobileWebFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync() {
        await _postgres.StartAsync();
        Factory = new SampleMobileWebFactory(_postgres.GetConnectionString());

        // Building the server runs Program: migrations, seeding and DI validation.
        _ = Factory.Server;
    }

    public async ValueTask DisposeAsync() {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public AsyncServiceScope CreateScope() => Factory.Services.CreateAsyncScope();

    public Task<HttpClient> CreateAliceClientAsync() => CreateAuthenticatedClientAsync(AliceUserName, AlicePassword);

    public Task<HttpClient> CreateBobClientAsync() => CreateAuthenticatedClientAsync(BobUserName, BobPassword);

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string userName, string password) {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/mobile/auth/login", new { userName, password }, Json);
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    public sealed class SampleMobileWebFactory(string connectionString) : WebApplicationFactory<Program> {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            // Development: demo seeding and the ephemeral JWT key, both used by these tests.
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:SampleMobile"] = connectionString,
                ["Database:ApplyMigrationsOnStartup"] = "true",
                ["Delivery:WorkerEnabled"] = "false",
                ["Outbox:Enabled"] = "false",
                ["Push:Simulated:Latency"] = "00:00:00",
                ["Firebase:CredentialsPath"] = "",
                ["Firebase:CredentialsJson"] = "",
                ["Firebase:UseApplicationDefaultCredentials"] = "false",
                ["DemoSeed:Enabled"] = "true",
                ["DemoSeed:Admin:UserName"] = "it-admin",
                ["DemoSeed:Admin:Password"] = "It#Admin2026",
                ["DemoSeed:MobileUsers:0:UserName"] = AliceUserName,
                ["DemoSeed:MobileUsers:0:Password"] = AlicePassword,
                ["DemoSeed:MobileUsers:0:DisplayName"] = "Integration Alice",
                ["DemoSeed:MobileUsers:1:UserName"] = BobUserName,
                ["DemoSeed:MobileUsers:1:Password"] = BobPassword,
                ["DemoSeed:MobileUsers:1:DisplayName"] = "Integration Bob",
                ["DemoSeed:VirtualDeviceUserName"] = AliceUserName
            }));
        }
    }
}
