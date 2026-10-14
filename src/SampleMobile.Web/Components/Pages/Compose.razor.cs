using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Features.DemoAlerts;
using SampleMobile.Application.Features.Devices;
using SampleMobile.Application.Features.Notifications;
using SampleMobile.Application.Features.Users;
using SampleMobile.Web.Components.Dashboard;
using SampleMobile.Web.Components.Shared;

namespace SampleMobile.Web.Components.Pages;

public partial class Compose {
    [Inject]
    private IDashboardSender Sender { get; set; } = null!;

    [PersistentState]
    public IReadOnlyList<UserSummary>? Users { get; set; }

    private IReadOnlyList<DeliveryTargetOption> Targets { get; set; } = [];
    private bool IsSending { get; set; }
    private bool IsRaising { get; set; }
    private ActionResultMessage? SendResult { get; set; }
    private ActionResultMessage? AlertResult { get; set; }

    protected override async Task OnInitializedAsync() {
        Users ??= (await Sender.SendAsync(new ListDemoUsersQuery())).Value ?? [];
    }

    private async Task LoadTargetsAsync(Guid? userId) {
        Targets = userId is { } id
            ? (await Sender.SendAsync(new ListDeliveryTargetsQuery(id))).Value ?? []
            : [];
    }

    private async Task SendAsync(ComposeNotificationModel model) {
        IsSending = true;
        try {
            var result = await Sender.SendAsync(new SendNotificationCommand(
                model.RecipientUserId!.Value,
                model.Type,
                model.Title,
                model.Body,
                model.ResourceId,
                model.ParseMetadata(),
                model.SelectedTargetIds.ToList()));

            SendResult = result.Succeeded
                ? ActionResultMessage.Success(
                    $"Notification {Display.ShortId(result.Value!.NotificationId)} saved with {result.Value.DeliveryJobIds.Count} delivery job(s).",
                    result.Value.DeliveryJobIds.Count > 0 ? $"deliveries?notificationId={result.Value.NotificationId}" : null,
                    "Watch its delivery history")
                : ActionResultMessage.Failure(result);
        } finally {
            IsSending = false;
        }
    }

    private async Task RaiseAlertAsync(RaiseDemoAlertModel model) {
        IsRaising = true;
        try {
            var result = await Sender.SendAsync(new RaiseDemoAlertCommand(model.RecipientUserId!.Value, model.Severity, model.Message));
            AlertResult = result.Succeeded
                ? ActionResultMessage.Success(
                    $"DemoAlert {Display.ShortId(result.Value!.AlertId)} saved; outbox event {Display.ShortId(result.Value.EventId)} queued.",
                    "deliveries",
                    "Watch the delivery history")
                : ActionResultMessage.Failure(result);
        } finally {
            IsRaising = false;
        }
    }
}
