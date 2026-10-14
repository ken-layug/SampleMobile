using System.Net;
using System.Net.Http.Headers;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.Api;

/// <summary>
/// Attaches the session's bearer token to every request except anonymous ones, and expires the session when
/// the backend answers 401 for that token. A 401 for an older token (or an override) leaves a newer session alone.
/// </summary>
public sealed class BearerTokenHandler(ISessionStore sessions) : DelegatingHandler {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        if (request.Options.TryGetValue(ApiRequestOptions.Anonymous, out var anonymous) && anonymous) {
            return await base.SendAsync(request, cancellationToken);
        }

        var isOverride = request.Options.TryGetValue(ApiRequestOptions.AccessTokenOverride, out var token) && !string.IsNullOrEmpty(token);
        if (!isOverride) {
            token = (await sessions.GetActiveAsync(cancellationToken))?.AccessToken;
        }

        if (string.IsNullOrEmpty(token)) {
            // Signed out: don't bother the backend with a request that can only fail.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isOverride) {
            await sessions.ExpireIfCurrentAsync(token);
        }

        return response;
    }
}
