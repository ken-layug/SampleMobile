using SampleMobile.Mobile.Core.Api.Contracts;

namespace SampleMobile.Mobile.Core.Api;

/// <summary><c>POST /api/mobile/auth/login</c> (anonymous).</summary>
public interface IAuthApiClient {
    Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Device registration endpoints. Provider-neutral: the provider is just a string in the body.</summary>
public interface IDeviceRegistrationApiClient {
    Task<ApiResult<DeviceRegistrationResponse>> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>DELETE /api/mobile/device-registrations/{installationId}?provider=…</c>. <paramref name="accessTokenOverride"/>
    /// lets a deferred logout cleanup use the signed-out user's token; a 401 for it never affects the current session.
    /// </summary>
    Task<ApiResult> UnregisterAsync(string installationId, string provider, string? accessTokenOverride = null, CancellationToken cancellationToken = default);
}

/// <summary>The inbox endpoints. The backend is the source of truth for notifications and read state.</summary>
public interface INotificationApiClient {
    Task<ApiResult<NotificationListResponse>> GetNotificationsAsync(int page = 1, int pageSize = 20, bool unreadOnly = false, CancellationToken cancellationToken = default);

    Task<ApiResult<NotificationResponse>> GetNotificationAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task<ApiResult<NotificationResponse>> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
}
