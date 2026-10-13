using System.Text.Json;
using System.Text.Json.Serialization;

namespace SampleMobile.Web.Common;

/// <summary>
/// JSON settings for the mobile API: camelCase properties, camelCase enum strings, and strict parsing
/// (no numbers in strings, exact property-name casing, no duplicate properties).
/// Nulls are written deliberately: the contract shows <c>"readAt": null</c> for unread notifications.
/// </summary>
public static class JsonDefaults {
    public static void Configure(JsonSerializerOptions options) {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.AllowDuplicateProperties = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
