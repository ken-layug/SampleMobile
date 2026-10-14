using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Features.Devices;

namespace SampleMobile.Web.Components.Pages;

public partial class Devices {
    private const int PageSize = 25;

    [Inject]
    private IDashboardSender Sender { get; set; } = null!;

    [PersistentState]
    public PagedResult<AdminDeviceRegistrationItem>? Data { get; set; }

    private int CurrentPage { get; set; } = 1;
    private bool IncludeDisabled { get; set; } = true;

    protected override async Task OnInitializedAsync() {
        if (Data is null) {
            await LoadAsync();
        }
    }

    private async Task LoadAsync() {
        var result = await Sender.SendAsync(new ListDeviceRegistrationsQuery(CurrentPage, PageSize, IncludeDisabled));
        Data = result.Value ?? Data;
    }

    private async Task GoToPageAsync(int page) {
        CurrentPage = page;
        await LoadAsync();
    }

    private async Task ToggleIncludeDisabledAsync(ChangeEventArgs args) {
        IncludeDisabled = args.Value is true;
        CurrentPage = 1;
        await LoadAsync();
    }
}
