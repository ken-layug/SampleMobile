using Microsoft.AspNetCore.Components;

namespace SampleMobile.Web.Components.Shared;

public partial class Pager {
    [Parameter, EditorRequired]
    public int Page { get; set; }

    [Parameter, EditorRequired]
    public int TotalPages { get; set; }

    [Parameter]
    public EventCallback<int> OnPageChanged { get; set; }

    private Task GoToAsync(int page) =>
        page < 1 || page > TotalPages || page == Page ? Task.CompletedTask : OnPageChanged.InvokeAsync(page);
}
