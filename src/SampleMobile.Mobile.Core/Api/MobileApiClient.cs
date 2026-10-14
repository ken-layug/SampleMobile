using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using SampleMobile.Mobile.Core.Api.Contracts;

namespace SampleMobile.Mobile.Core.Api;

/// <summary>
/// HTTP client for the mobile API (<c>docs/mobile-api-contract.md</c>). URIs are built per request from
/// <see cref="IBackendSettings"/>, so the base URL can change at runtime. Expected failures become
/// <see cref="ApiResult"/> values; only caller cancellation throws.
/// </summary>
public sealed class MobileApiClient(IHttpClientFactory httpClientFactory, IBackendSettings backend, ILogger<MobileApiClient> logger)
    : IAuthApiClient, IDeviceRegistrationApiClient, INotificationApiClient {
    public const string HttpClientName = "SampleMobile.MobileApi";

    private static readonly MobileJsonContext Json = MobileJsonContext.Default;

    public Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/mobile/auth/login", JsonContent.Create(request, Json.LoginRequest), Json.LoginResponse, anonymous: true, accessTokenOverride: null, cancellationToken);

    public Task<ApiResult<DeviceRegistrationResponse>> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/mobile/device-registrations", JsonContent.Create(request, Json.RegisterDeviceRequest), Json.DeviceRegistrationResponse, anonymous: false, accessTokenOverride: null, cancellationToken);

    public async Task<ApiResult> UnregisterAsync(string installationId, string provider, string? accessTokenOverride = null, CancellationToken cancellationToken = default) {
        var path = $"api/mobile/device-registrations/{Uri.EscapeDataString(installationId)}?provider={Uri.EscapeDataString(provider)}";
        return await SendAsync<object>(HttpMethod.Delete, path, content: null, responseType: null, anonymous: false, accessTokenOverride, cancellationToken);
    }

    public Task<ApiResult<NotificationListResponse>> GetNotificationsAsync(int page = 1, int pageSize = 20, bool unreadOnly = false, CancellationToken cancellationToken = default) {
        var path = string.Create(CultureInfo.InvariantCulture, $"api/mobile/notifications?page={page}&pageSize={pageSize}&unreadOnly={(unreadOnly ? "true" : "false")}");
        return SendAsync(HttpMethod.Get, path, content: null, Json.NotificationListResponse, anonymous: false, accessTokenOverride: null, cancellationToken);
    }

    public Task<ApiResult<NotificationResponse>> GetNotificationAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, $"api/mobile/notifications/{notificationId:D}", content: null, Json.NotificationResponse, anonymous: false, accessTokenOverride: null, cancellationToken);

    public Task<ApiResult<NotificationResponse>> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/mobile/notifications/{notificationId:D}/read", content: null, Json.NotificationResponse, anonymous: false, accessTokenOverride: null, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        JsonTypeInfo<T>? responseType,
        bool anonymous,
        string? accessTokenOverride,
        CancellationToken cancellationToken) {
        if (backend.BaseUri is not { } baseUri) {
            return ApiResult<T>.Fail(ApiFailureKind.NotConfigured, "No backend URL is configured.");
        }

        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath)) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        if (anonymous) {
            request.Options.Set(ApiRequestOptions.Anonymous, true);
        }

        if (accessTokenOverride is not null) {
            request.Options.Set(ApiRequestOptions.AccessTokenOverride, accessTokenOverride);
        }

        HttpResponseMessage response;
        try {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return ApiResult<T>.Fail(ApiFailureKind.Network, "The request timed out.");
        } catch (HttpRequestException ex) {
            logger.LogInformation("{Method} {Path} failed: {Error}", method, relativePath, ex.Message);
            return ApiResult<T>.Fail(ApiFailureKind.Network, ex.Message);
        }

        using (response) {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode) {
                return await ReadSuccessAsync(response, responseType, status, cancellationToken);
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            logger.LogInformation("{Method} {Path} returned {Status}", method, relativePath, status);
            return ApiResult<T>.Fail(Classify(response.StatusCode), problem?.Detail ?? problem?.Title ?? response.ReasonPhrase, status, problem?.Errors, problem?.TraceId);
        }
    }

    private static async Task<ApiResult<T>> ReadSuccessAsync<T>(HttpResponseMessage response, JsonTypeInfo<T>? responseType, int status, CancellationToken cancellationToken) {
        if (responseType is null) {
            return ApiResult<T>.Success(default!, status);
        }

        try {
            var value = await response.Content.ReadFromJsonAsync(responseType, cancellationToken);
            return value is null
                ? ApiResult<T>.Fail(ApiFailureKind.InvalidResponse, "The response body was empty.", status)
                : ApiResult<T>.Success(value, status);
        } catch (JsonException ex) {
            return ApiResult<T>.Fail(ApiFailureKind.InvalidResponse, $"The response did not match the contract: {ex.Message}", status);
        } catch (NotSupportedException ex) {
            return ApiResult<T>.Fail(ApiFailureKind.InvalidResponse, ex.Message, status);
        }
    }

    private static async Task<ProblemDetailsResponse?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        try {
            return await response.Content.ReadFromJsonAsync(Json.ProblemDetailsResponse, cancellationToken);
        } catch (Exception ex) when (ex is JsonException or NotSupportedException) {
            return null;
        }
    }

    private static ApiFailureKind Classify(HttpStatusCode status) => status switch {
        HttpStatusCode.BadRequest => ApiFailureKind.Invalid,
        HttpStatusCode.Unauthorized => ApiFailureKind.Unauthorized,
        HttpStatusCode.NotFound => ApiFailureKind.NotFound,
        HttpStatusCode.Conflict => ApiFailureKind.Conflict,
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => ApiFailureKind.Server,
        >= HttpStatusCode.InternalServerError => ApiFailureKind.Server,
        _ => ApiFailureKind.Unexpected
    };
}
