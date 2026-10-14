using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Features.Notifications;

namespace SampleMobile.Web.Components.Pages;

public partial class Overview {
    [Inject]
    private IDashboardSender Sender { get; set; } = null!;

    [PersistentState]
    public DashboardOverview? Data { get; set; }

    private string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync() {
        Data ??= await LoadAsync();
    }

    private async Task RefreshAsync() {
        Data = await LoadAsync() ?? Data;
    }

    private async Task<DashboardOverview?> LoadAsync() {
        var result = await Sender.SendAsync(new GetDashboardOverviewQuery());
        ErrorMessage = result.Succeeded ? null : "Could not load the overview.";
        return result.Value;
    }
}
