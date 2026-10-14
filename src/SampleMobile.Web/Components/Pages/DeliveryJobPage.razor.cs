using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Features.Delivery;
using SampleMobile.Web.Components.Shared;

namespace SampleMobile.Web.Components.Pages;

public partial class DeliveryJobPage {
    [Inject]
    private IDashboardSender Sender { get; set; } = null!;

    [Parameter]
    public Guid JobId { get; set; }

    [PersistentState]
    public DeliveryJobDetail? Detail { get; set; }

    private bool NotFound { get; set; }
    private bool IsRequeuing { get; set; }
    private ActionResultMessage? RequeueResult { get; set; }

    protected override async Task OnParametersSetAsync() {
        if (Detail?.Job.JobId != JobId) {
            await LoadAsync();
        }
    }

    private async Task LoadAsync() {
        var result = await Sender.SendAsync(new GetDeliveryJobDetailQuery(JobId));
        NotFound = result.Status == OperationStatus.NotFound;
        Detail = result.Value ?? (NotFound ? null : Detail);
    }

    private async Task RequeueAsync() {
        IsRequeuing = true;
        try {
            var result = await Sender.SendAsync(new RequeueDeliveryJobCommand(JobId));
            RequeueResult = result.Succeeded
                ? ActionResultMessage.Success("Job requeued. The worker picks it up immediately.")
                : ActionResultMessage.Failure(result);
            await LoadAsync();
        } finally {
            IsRequeuing = false;
        }
    }
}
