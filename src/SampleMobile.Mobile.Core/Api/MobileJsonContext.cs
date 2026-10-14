using System.Text.Json;
using System.Text.Json.Serialization;
using SampleMobile.Mobile.Core.Api.Contracts;

namespace SampleMobile.Mobile.Core.Api;

/// <summary>
/// Source-generated (trim-safe) serializer for the mobile API contract. The options mirror the backend's
/// <c>JsonDefaults</c>: camelCase, case-sensitive names, numbers never read from strings, nulls always written.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNameCaseInsensitive = false,
    NumberHandling = JsonNumberHandling.Strict,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(RegisterDeviceRequest))]
[JsonSerializable(typeof(DeviceRegistrationResponse))]
[JsonSerializable(typeof(NotificationResponse))]
[JsonSerializable(typeof(NotificationListResponse))]
[JsonSerializable(typeof(ProblemDetailsResponse))]
public sealed partial class MobileJsonContext : JsonSerializerContext;
