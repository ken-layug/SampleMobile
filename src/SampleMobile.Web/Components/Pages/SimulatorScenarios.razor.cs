using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Features.Delivery;
using SampleMobile.Application.Features.Simulation;
using SampleMobile.Application.Features.Users;
using SampleMobile.Web.Components.Shared;

namespace SampleMobile.Web.Components.Pages;

public partial class SimulatorScenarios {
    private static readonly IReadOnlyList<ScenarioDescription> Scenarios = [
        new(SimulatedScenario.Accept,
            "Simulated acceptance",
            "The simulator accepts the first attempt.",
            "1 attempt, status Provider accepted, flagged Simulated."),
        new(SimulatedScenario.TransientThenAccept,
            "Transient failure, then retry",
            "The simulator reports 'temporarily unavailable' twice, then accepts.",
            "Retry scheduled twice with growing backoff, then Provider accepted on attempt 3."),
        new(SimulatedScenario.PermanentFailure,
            "Permanent failure",
            "The simulator rejects the message permanently.",
            "1 attempt, status Failed, no retry. The notification stays in the inbox."),
        new(SimulatedScenario.InvalidRegistration,
            "Invalid device registration",
            "The simulator reports the device address as unregistered.",
            "Status Failed and the virtual device is disabled (invalidated by provider).")
    ];

    [Inject]
    private IDashboardSender Sender { get; set; } = null!;

    [PersistentState]
    public IReadOnlyList<UserSummary>? Users { get; set; }

    private Guid RecipientUserId { get; set; }
    private IReadOnlyList<DeliveryJobSummary> Jobs { get; set; } = [];
    private IReadOnlyList<SimulatedPushRecord> Messages { get; set; } = [];
    private bool IsRunning { get; set; }
    private ActionResultMessage? RunResult { get; set; }

    protected override async Task OnInitializedAsync() {
        Users ??= (await Sender.SendAsync(new ListDemoUsersQuery())).Value ?? [];

        // Default to a mobile demo user rather than the administrator.
        RecipientUserId = (Users.FirstOrDefault(user => !user.Roles.Contains(Roles.Admin)) ?? Users.FirstOrDefault())?.Id ?? Guid.Empty;
        await RefreshAsync();
    }

    private async Task RefreshAsync() {
        var jobs = await Sender.SendAsync(new ListDeliveryHistoryQuery(1, 10, SimulatedOnly: true));
        Jobs = jobs.Value?.Items ?? Jobs;
        Messages = (await Sender.SendAsync(new ListSimulatedMessagesQuery(25))).Value ?? Messages;
    }

    private async Task RunAsync(SimulatedScenario scenario) {
        IsRunning = true;
        try {
            var result = await Sender.SendAsync(new RunSimulatedScenarioCommand(RecipientUserId, scenario));
            RunResult = result.Succeeded
                ? ActionResultMessage.Success(
                    $"Started '{RunSimulatedScenarioCommandHandler.Describe(scenario)}' for notification {Display.ShortId(result.Value!.NotificationId)}.",
                    result.Value.DeliveryJobId is { } jobId ? $"deliveries/{jobId}" : null,
                    "Follow its attempts")
                : ActionResultMessage.Failure(result);
            await RefreshAsync();
        } finally {
            IsRunning = false;
        }
    }

    private sealed record ScenarioDescription(SimulatedScenario Scenario, string Title, string Description, string Expected);
}
