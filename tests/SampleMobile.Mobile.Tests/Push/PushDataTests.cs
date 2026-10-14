using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Tests.Push;

/// <summary>Reading the backend's push data keys, and masking provider addresses.</summary>
public class PushDataTests {
    [Theory]
    [InlineData("01a137b2-10b3-7eb7-a480-92b0b0848c5c", true)]
    [InlineData(" 01A137B2-10B3-7EB7-A480-92B0B0848C5C ", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("42", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_non_empty_guid_counts_as_a_notification_id(string? raw, bool valid) {
        var data = new Dictionary<string, string>();
        if (raw is not null) {
            data[PushDataKeys.NotificationId] = raw;
        }

        var parsed = PushDataParser.Parse(data);

        Assert.Equal(valid, parsed.HasNotificationId);
    }

    [Fact]
    public void Type_and_resource_id_are_read_and_provider_keys_are_ignored() {
        var parsed = PushDataParser.Parse(new Dictionary<string, string> {
            [PushDataKeys.NotificationId] = "01a137b2-10b3-7eb7-a480-92b0b0848c5c",
            [PushDataKeys.Type] = "demo.alert",
            [PushDataKeys.ResourceId] = "alert-7",
            ["google.message_id"] = "0:123"
        });

        Assert.Equal(("demo.alert", "alert-7"), (parsed.Type, parsed.ResourceId));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("short-token", "•••••••••••")]
    [InlineData("exactly-12ch", "••••••••••••")]
    [InlineData("fcm-registration-token-value", "fcm-re…alue")]
    public void Addresses_are_masked_like_the_backend_does(string? address, string expected) {
        Assert.Equal(expected, TokenMasker.Mask(address));
    }
}
