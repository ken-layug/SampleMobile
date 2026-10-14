using System.Text.Json;
using System.Text.Json.Nodes;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Api.Contracts;

namespace SampleMobile.Mobile.Tests.Api;

/// <summary>The client's JSON matches docs/mobile-api-contract.md byte for byte where it matters.</summary>
public class ContractJsonTests {
    private static readonly MobileJsonContext Json = MobileJsonContext.Default;

    [Fact]
    public void A_device_registration_serializes_to_exactly_the_four_contract_properties() {
        var request = new RegisterDeviceRequest("0b6f0c7e-2b8f-4f4e-9a37-3c1f6c7f5a10", "android", "fcm", "provider-registration-token");

        var json = JsonNode.Parse(JsonSerializer.Serialize(request, Json.RegisterDeviceRequest))!.AsObject();

        Assert.Equal(["installationId", "platform", "provider", "address"], json.Select(property => property.Key));
        Assert.Equal("0b6f0c7e-2b8f-4f4e-9a37-3c1f6c7f5a10", (string?)json["installationId"]);
        Assert.Equal("android", (string?)json["platform"]);
        Assert.Equal("fcm", (string?)json["provider"]);
        Assert.Equal("provider-registration-token", (string?)json["address"]);
    }

    [Fact]
    public void The_contract_notification_example_deserializes_including_microseconds_and_null_read_time() {
        const string body = """
            {"id":"01a137b2-10b3-7eb7-a480-92b0b0848c5c","type":"demo.alert","title":"Warning alert",
             "body":"Disk usage on the demo server crossed 90%.","createdAt":"2026-10-13T10:00:00.123456+00:00",
             "readAt":null,"resourceId":"01a137b2-10b3-7eb7-a480-92b0b0848c5c","metadata":{"severity":"warning","source":"demo-alert"}}
            """;

        var notification = JsonSerializer.Deserialize(body, Json.NotificationResponse)!;

        Assert.Equal(Guid.Parse("01a137b2-10b3-7eb7-a480-92b0b0848c5c"), notification.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero).AddTicks(1_234_560), notification.CreatedAt);
        Assert.Null(notification.ReadAt);
        Assert.Equal("warning", notification.Metadata!["severity"]);
    }

    [Fact]
    public void A_list_page_and_a_login_response_deserialize() {
        var list = JsonSerializer.Deserialize("""{"items":[],"page":1,"pageSize":20,"totalCount":42,"unreadCount":3}""", Json.NotificationListResponse)!;
        var login = JsonSerializer.Deserialize("""{"accessToken":"eyJ","tokenType":"Bearer","expiresAt":"2026-10-13T18:00:00+00:00","expiresIn":28800}""", Json.LoginResponse)!;

        Assert.Equal((42, 3), (list.TotalCount, list.UnreadCount));
        Assert.Equal(("Bearer", 28800), (login.TokenType, login.ExpiresIn));
    }

    [Fact]
    public void Numbers_sent_as_strings_are_rejected_like_the_backend_does() {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"items":[],"page":"1","pageSize":20,"totalCount":0,"unreadCount":0}""", Json.NotificationListResponse));
    }

    [Fact]
    public void Property_names_are_case_sensitive() {
        var login = JsonSerializer.Deserialize("""{"AccessToken":"x","tokenType":"Bearer","expiresAt":"2026-10-13T18:00:00+00:00","expiresIn":1}""", Json.LoginResponse)!;

        Assert.Null(login.AccessToken);
    }

    [Fact]
    public void A_validation_problem_deserializes_with_its_errors() {
        const string body = """
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,
             "errors":{"provider":["Provider 'onesignal' is not supported. Supported providers: fcm, simulated."]},"traceId":"00-abc"}
            """;

        var problem = JsonSerializer.Deserialize(body, Json.ProblemDetailsResponse)!;

        Assert.Equal(400, problem.Status);
        Assert.Contains("onesignal", problem.Errors!["provider"].Single(), StringComparison.Ordinal);
        Assert.Equal("00-abc", problem.TraceId);
    }
}
