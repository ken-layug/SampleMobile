using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Application.Common;
using SampleMobile.Application.Features.Delivery;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Web.Components.Pages;

public partial class DeliveryHistory {
    private const int PageSize = 25;

    [Inject]
    private IDashboardSender Sender { get; set; } = null!;

    [SupplyParameterFromQuery]
    private Guid? NotificationId { get; set; }

    [PersistentState]
    public PagedResult<DeliveryJobSummary>? Data { get; set; }

    private int CurrentPage { get; set; } = 1;
    private DeliveryJobStatus? Status { get; set; }
    private string? ProviderId { get; set; }
    private bool SimulatedOnly { get; set; }

    protected override async Task OnInitializedAsync() {
        if (Data is null) {
            await LoadAsync();
        }
    }

    private async Task LoadAsync() {
        var result = await Sender.SendAsync(
            new ListDeliveryHistoryQuery(CurrentPage, PageSize, Status, ProviderId, NotificationId, SimulatedOnly));
        Data = result.Value ?? Data;
    }

    private async Task GoToPageAsync(int page) {
        CurrentPage = page;
        await LoadAsync();
    }

    private Task ChangeStatusAsync(ChangeEventArgs args) {
        Status = Enum.TryParse<DeliveryJobStatus>(args.Value?.ToString(), out var status) ? status : null;
        return ReloadFromFirstPageAsync();
    }

    private Task ChangeProviderAsync(ChangeEventArgs args) {
        var value = args.Value?.ToString();
        ProviderId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return ReloadFromFirstPageAsync();
    }

    private Task ChangeSimulatedOnlyAsync(ChangeEventArgs args) {
        SimulatedOnly = args.Value is true;
        return ReloadFromFirstPageAsync();
    }

    private Task ReloadFromFirstPageAsync() {
        CurrentPage = 1;
        return LoadAsync();
    }
}
