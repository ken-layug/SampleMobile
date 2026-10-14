using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Push.Simulated;

namespace SampleMobile.Tests.Infrastructure;

public class SimulatedPushNotificationProviderTests {
    private readonly SimulatedPushControl _control = new();

    [Fact]
    public async Task The_simulator_records_each_dispatch_and_flags_it_as_simulated() {
        var request = Request("sim-address-0123456789");

        var result = await Provider().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(DispatchOutcome.Accepted, result.Outcome);
        Assert.True(result.IsSimulated);
        Assert.StartsWith("sim-", result.ProviderMessageId);
        var record = Assert.Single(_control.GetRecentMessages(10));
        Assert.Equal(request.DeliveryJobId, record.DeliveryJobId);
        Assert.Equal(result.ProviderMessageId, record.SimulatedMessageId);
        Assert.DoesNotContain("0123456789", record.MaskedAddress);
    }

    [Fact]
    public async Task Scripted_outcomes_are_used_in_order_then_the_simulator_accepts() {
        const string address = "sim-scripted-address-1";
        _control.ScriptOutcomes(address, [DispatchOutcome.TransientFailure, DispatchOutcome.InvalidRegistration]);
        var provider = Provider();

        var first = await provider.SendAsync(Request(address), TestContext.Current.CancellationToken);
        var second = await provider.SendAsync(Request(address), TestContext.Current.CancellationToken);
        var third = await provider.SendAsync(Request(address), TestContext.Current.CancellationToken);

        Assert.Equal(DispatchOutcome.TransientFailure, first.Outcome);
        Assert.Equal(DispatchOutcome.InvalidRegistration, second.Outcome);
        Assert.Equal(DispatchOutcome.Accepted, third.Outcome);
        Assert.All([first, second, third], result => Assert.True(result.IsSimulated));
    }

    [Fact]
    public async Task Scripts_only_affect_their_own_address() {
        _control.ScriptOutcomes("sim-failing-address", [DispatchOutcome.PermanentFailure]);

        var result = await Provider().SendAsync(Request("sim-other-address"), TestContext.Current.CancellationToken);

        Assert.Equal(DispatchOutcome.Accepted, result.Outcome);
    }

    [Fact]
    public void The_simulator_describes_itself_as_simulated() {
        var descriptor = Provider().Describe();

        Assert.True(descriptor.IsSimulated);
        Assert.Equal("simulated", descriptor.ProviderId);
    }

    private SimulatedPushNotificationProvider Provider() =>
        new(
            _control,
            Options.Create(new SimulatedProviderOptions { Latency = TimeSpan.Zero }),
            TimeProvider.System,
            NullLogger<SimulatedPushNotificationProvider>.Instance);

    private static PushDispatchRequest Request(string address) =>
        new(
            Guid.CreateVersion7(),
            1,
            new PushTarget(Guid.CreateVersion7(), "simulated", DevicePlatform.Virtual, address),
            new PushMessage(Guid.CreateVersion7(), "demo.message", "Title", "Body", null));
}
