using SampleMobile.Domain.Entities;
using SampleMobile.Domain.Events;

namespace SampleMobile.Tests.Domain;

public class DemoAlertTests {
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creating_an_alert_raises_DemoAlertRaised_with_the_alert_data() {
        var recipientId = Guid.CreateVersion7();

        var alert = DemoAlert.Create(recipientId, DemoAlertSeverity.Critical, "  Disk usage above 90%  ", Now);

        var raised = Assert.IsType<DemoAlertRaised>(Assert.Single(alert.DomainEvents));
        Assert.Equal(alert.Id, raised.AlertId);
        Assert.Equal(recipientId, raised.RecipientUserId);
        Assert.Equal(DemoAlertSeverity.Critical, raised.Severity);
        Assert.Equal("Disk usage above 90%", raised.Message);
        Assert.Equal(Now, raised.OccurredOn);
    }

    [Fact]
    public void An_alert_without_a_recipient_is_a_programming_error() {
        Assert.Throws<ArgumentException>(() => DemoAlert.Create(Guid.Empty, DemoAlertSeverity.Info, "message", Now));
    }
}
