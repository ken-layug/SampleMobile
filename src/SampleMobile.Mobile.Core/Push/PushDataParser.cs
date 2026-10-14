namespace SampleMobile.Mobile.Core.Push;

/// <summary>
/// The data keys the backend puts in every push (see the contract's "Push payload" section). They belong to the
/// backend contract, not to any provider, and the app reuses them for its own notification intents.
/// </summary>
public static class PushDataKeys {
    public const string NotificationId = "notificationId";
    public const string Type = "type";
    public const string ResourceId = "resourceId";
}

/// <summary>Push data after parsing. <see cref="NotificationId"/> is null when the raw value is missing or not a GUID.</summary>
public sealed record PushData(Guid? NotificationId, string? RawNotificationId, string? Type, string? ResourceId) {
    public bool HasNotificationId => NotificationId is not null;
}

/// <summary>Reads backend push data from a provider's string map (FCM data, Android intent extras…).</summary>
public static class PushDataParser {
    public static PushData Parse(IReadOnlyDictionary<string, string> data) {
        var raw = Value(data, PushDataKeys.NotificationId);
        Guid? id = Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty ? parsed : null;
        return new PushData(id, raw, Value(data, PushDataKeys.Type), Value(data, PushDataKeys.ResourceId));
    }

    private static string? Value(IReadOnlyDictionary<string, string> data, string key) =>
        data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
