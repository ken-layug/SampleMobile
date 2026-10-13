namespace SampleMobile.Application.Common;

/// <summary>The notification types this POC knows how to produce. Clients should ignore types they don't recognize.</summary>
public static class NotificationTypes {
    public const string DemoAlert = "demo.alert";
    public const string DemoMessage = "demo.message";
    public const string SystemAnnouncement = "system.announcement";
    public const string DemoSimulation = "demo.simulation";

    /// <summary>Types an administrator can pick when composing a notification by hand.</summary>
    public static IReadOnlyList<string> Composable { get; } = [DemoMessage, DemoAlert, SystemAnnouncement];
}
