using System.Globalization;

namespace SampleMobile.Mobile.Tests.Fakes;

/// <summary>Response bodies shaped exactly like the backend's (see docs/mobile-api-contract.md).</summary>
public static class BackendJson {
    public static string Login(string token, DateTimeOffset expiresAt) =>
        $$"""{"accessToken":"{{token}}","tokenType":"Bearer","expiresAt":"{{Iso(expiresAt)}}","expiresIn":28800}""";

    public static string Registration(string provider = "fcm") =>
        $$"""
        {"id":"01a137b6-f940-7e9b-a438-0b5927a6002d","installationId":"0b6f0c7e-2b8f-4f4e-9a37-3c1f6c7f5a10","platform":"android",
         "provider":"{{provider}}","enabled":true,"disabledReason":null,"createdAt":"2026-10-13T10:00:00+00:00",
         "updatedAt":"2026-10-13T10:00:00+00:00","lastSeenAt":"2026-10-13T10:00:00+00:00"}
        """;

    public static string Notification(Guid id, string title = "Warning alert", DateTimeOffset? readAt = null, DateTimeOffset? createdAt = null) =>
        $$$"""
        {"id":"{{{id}}}","type":"demo.alert","title":"{{{title}}}","body":"Disk usage on the demo server crossed 90%.",
         "createdAt":"{{{Iso(createdAt ?? new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero))}}}",
         "readAt":{{{ReadAt(readAt)}}},"resourceId":null,"metadata":{"severity":"warning"}}
        """;

    private static string ReadAt(DateTimeOffset? readAt) => readAt is { } read ? $"\"{Iso(read)}\"" : "null";

    public static string List(int unreadCount = 0, int? totalCount = null, params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"page":1,"pageSize":50,"totalCount":{{totalCount ?? items.Length}},"unreadCount":{{unreadCount}}}""";

    private static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
}
