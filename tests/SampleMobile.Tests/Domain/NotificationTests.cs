using SampleMobile.Domain.Entities;

namespace SampleMobile.Tests.Domain;

public class NotificationTests {
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Each_recipient_has_independent_read_state() {
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();
        var notification = Create([alice, bob]);

        notification.MarkReadBy(alice, Now.AddMinutes(1));

        Assert.Equal(Now.AddMinutes(1), notification.FindRecipient(alice)!.ReadAt);
        Assert.Null(notification.FindRecipient(bob)!.ReadAt);
    }

    [Fact]
    public void Marking_read_twice_keeps_the_first_read_time() {
        var alice = Guid.CreateVersion7();
        var notification = Create([alice]);

        notification.MarkReadBy(alice, Now.AddMinutes(1));
        var recipient = notification.MarkReadBy(alice, Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), recipient.ReadAt);
    }

    [Fact]
    public void Duplicate_recipients_are_collapsed() {
        var alice = Guid.CreateVersion7();

        var notification = Create([alice, alice]);

        Assert.Single(notification.Recipients);
    }

    [Fact]
    public void A_non_recipient_cannot_mark_a_notification_read() {
        var notification = Create([Guid.CreateVersion7()]);

        Assert.Throws<InvalidOperationException>(() => notification.MarkReadBy(Guid.CreateVersion7(), Now));
    }

    [Theory]
    [InlineData("demo.alert", true)]
    [InlineData("system.announcement", true)]
    [InlineData("Demo.Alert", false)]
    [InlineData("demo..alert", false)]
    [InlineData("", false)]
    public void Notification_types_are_lower_case_dotted_segments(string value, bool valid) {
        Assert.Equal(valid, NotificationType.TryCreate(value, out _));
    }

    private static Notification Create(IReadOnlyCollection<Guid> recipients) =>
        Notification.Create(NotificationType.Create("demo.message"), "Title", "Body", null, null, recipients, null, Now);
}
