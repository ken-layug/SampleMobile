using System.Net;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Api.Contracts;
using SampleMobile.Mobile.Core.Session;
using SampleMobile.Mobile.Tests.Fakes;

namespace SampleMobile.Mobile.Tests.Api;

/// <summary>The API client over the real HTTP pipeline: auth header handling and error mapping.</summary>
public class MobileApiClientTests : IDisposable {
    private readonly MobileTestHost _host = new();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private MobileApiClient Client => _host.Get<MobileApiClient>();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Login_is_sent_without_an_authorization_header() {
        await Client.LoginAsync(new LoginRequest("alice", "secret"), Cancellation);

        var login = _host.Http.RequestsTo(HttpMethod.Post, "/api/mobile/auth/login").Single();
        Assert.Null(login.Authorization);
        Assert.Equal("""{"userName":"alice","password":"secret"}""", login.Body);
    }

    [Fact]
    public async Task Protected_calls_carry_the_sessions_bearer_token() {
        await _host.SignInAsync();

        await Client.GetNotificationsAsync(cancellationToken: Cancellation);

        var request = _host.Http.Requests.Last();
        Assert.Equal("Bearer token-alice-1", request.Authorization);
        Assert.Equal("?page=1&pageSize=20&unreadOnly=false", request.Query);
    }

    [Fact]
    public async Task A_protected_call_while_signed_out_never_reaches_the_backend() {
        var result = await Client.GetNotificationsAsync(cancellationToken: Cancellation);

        Assert.Equal(ApiFailureKind.Unauthorized, result.Failure);
        Assert.Empty(_host.Http.Requests);
    }

    [Fact]
    public async Task A_validation_problem_becomes_an_invalid_result_with_the_field_errors() {
        await _host.SignInAsync();
        _host.Http.On(HttpMethod.Post, "/api/mobile/device-registrations",
            _ => StubHttpHandler.Problem(HttpStatusCode.BadRequest, "Validation failed", """{"provider":["Provider 'x' is not supported."]}"""));

        var result = await Client.RegisterAsync(new RegisterDeviceRequest("id", "android", "x", "address"), Cancellation);

        Assert.Equal(ApiFailureKind.Invalid, result.Failure);
        Assert.False(result.IsTransient);
        Assert.Equal("Provider 'x' is not supported.", result.Errors["provider"].Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiFailureKind.NotFound, false)]
    [InlineData(HttpStatusCode.Conflict, ApiFailureKind.Conflict, true)]
    [InlineData(HttpStatusCode.InternalServerError, ApiFailureKind.Server, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiFailureKind.Server, true)]
    [InlineData(HttpStatusCode.Forbidden, ApiFailureKind.Unexpected, false)]
    public async Task Problem_statuses_map_to_failure_kinds(HttpStatusCode status, ApiFailureKind expected, bool transient) {
        await _host.SignInAsync();
        var id = Guid.NewGuid();
        _host.Http.On(HttpMethod.Get, $"/api/mobile/notifications/{id}", _ => StubHttpHandler.Problem(status, "Nope"));

        var result = await Client.GetNotificationAsync(id, Cancellation);

        Assert.Equal(expected, result.Failure);
        Assert.Equal(transient, result.IsTransient);
        Assert.Equal((int)status, result.StatusCode);
        Assert.Equal("00-trace", result.TraceId);
    }

    [Fact]
    public async Task An_unreachable_backend_is_a_transient_network_failure() {
        _host.Http.Offline(HttpMethod.Post, "/api/mobile/auth/login");

        var result = await Client.LoginAsync(new LoginRequest("alice", "secret"), Cancellation);

        Assert.Equal(ApiFailureKind.Network, result.Failure);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public async Task A_timeout_is_a_network_failure_not_an_exception() {
        _host.Http.On(HttpMethod.Post, "/api/mobile/auth/login",
            (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")));

        var result = await Client.LoginAsync(new LoginRequest("alice", "secret"), Cancellation);

        Assert.Equal(ApiFailureKind.Network, result.Failure);
    }

    [Fact]
    public async Task A_body_that_breaks_the_contract_is_an_invalid_response() {
        _host.Http.On(HttpMethod.Post, "/api/mobile/auth/login", HttpStatusCode.OK, """{"accessToken":"x","expiresIn":"soon"}""");

        var result = await Client.LoginAsync(new LoginRequest("alice", "secret"), Cancellation);

        Assert.Equal(ApiFailureKind.InvalidResponse, result.Failure);
    }

    [Fact]
    public async Task Unregister_uses_the_override_token_and_names_the_provider() {
        await _host.SignInAsync();

        var result = await Client.UnregisterAsync(_host.InstallationId, "fcm", accessTokenOverride: "token-old", Cancellation);

        Assert.True(result.Succeeded);
        var delete = _host.Http.RequestsTo(HttpMethod.Delete, $"/api/mobile/device-registrations/{_host.InstallationId}").Single();
        Assert.Equal("Bearer token-old", delete.Authorization);
        Assert.Equal("?provider=fcm", delete.Query);
    }

    [Fact]
    public async Task A_401_for_an_override_token_leaves_the_current_session_alone() {
        await _host.SignInAsync();
        _host.Http.On(HttpMethod.Delete, $"/api/mobile/device-registrations/{_host.InstallationId}", _ => StubHttpHandler.Problem(HttpStatusCode.Unauthorized, "Expired"));

        await Client.UnregisterAsync(_host.InstallationId, "fcm", accessTokenOverride: "token-old", Cancellation);

        Assert.NotNull(_host.Get<ISessionStore>().Current);
    }

    [Fact]
    public async Task Changing_the_backend_url_takes_effect_without_a_restart() {
        var settings = _host.Get<IBackendSettings>();

        Assert.Null(settings.TrySetBaseUrl("http://192.168.1.20:5010"));
        await Client.LoginAsync(new LoginRequest("alice", "secret"), Cancellation);

        Assert.Equal(new Uri("http://192.168.1.20:5010/"), settings.BaseUri);
        Assert.NotNull(settings.TrySetBaseUrl("ftp://nope"));
    }
}
