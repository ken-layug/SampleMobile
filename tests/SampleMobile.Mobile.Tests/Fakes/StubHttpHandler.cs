using System.Net;
using System.Text;

namespace SampleMobile.Mobile.Tests.Fakes;

/// <summary>A request as the backend would have received it.</summary>
public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Authorization, string? Body);

/// <summary>
/// Stands in for the backend at the bottom of the real HTTP pipeline (so <c>BearerTokenHandler</c> runs).
/// Routes match on method and path; the latest registration for a route wins. Every request is recorded.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = [];
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests {
        get {
            lock (_sync) {
                return _requests.ToList();
            }
        }
    }

    public void On(HttpMethod method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) {
        lock (_sync) {
            _routes[Key(method, path)] = respond;
        }
    }

    public void On(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        On(method, path, request => Task.FromResult(respond(request)));

    public void On(HttpMethod method, string path, HttpStatusCode status, string? json = null) =>
        On(method, path, _ => Json(status, json));

    /// <summary>Makes the route fail as if the device were offline.</summary>
    public void Offline(HttpMethod method, string path) =>
        On(method, path, (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("Network is unreachable")));

    public IReadOnlyList<RecordedRequest> RequestsTo(HttpMethod method, string path) =>
        Requests.Where(request => request.Method == method && request.Path == path).ToList();

    public static HttpResponseMessage Json(HttpStatusCode status, string? json) {
        var response = new HttpResponseMessage(status);
        if (json is not null) {
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return response;
    }

    public static HttpResponseMessage Problem(HttpStatusCode status, string detail, string? errorsJson = null) {
        var errors = errorsJson is null ? string.Empty : $",\"errors\":{errorsJson}";
        var json = $"{{\"type\":\"about:blank\",\"title\":\"{status}\",\"status\":{(int)status},\"detail\":\"{detail}\",\"traceId\":\"00-trace\"{errors}}}";
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/problem+json");
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var uri = request.RequestUri!;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? respond;
        lock (_sync) {
            _requests.Add(new RecordedRequest(request.Method, uri.AbsolutePath, uri.Query, request.Headers.Authorization?.ToString(), body));
            _routes.TryGetValue(Key(request.Method, uri.AbsolutePath), out respond);
        }

        if (respond is null) {
            throw new InvalidOperationException($"No stub route for {request.Method} {uri.AbsolutePath}");
        }

        var response = await respond(request);
        response.RequestMessage = request;
        return response;
    }

    private static string Key(HttpMethod method, string path) => $"{method.Method} {path}";
}
