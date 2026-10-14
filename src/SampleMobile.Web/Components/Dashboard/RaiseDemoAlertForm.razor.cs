using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Identity;

namespace SampleMobile.Web.Components.Dashboard;

public partial class RaiseDemoAlertForm {
    private readonly RaiseDemoAlertModel _model = new();

    [Parameter, EditorRequired]
    public IReadOnlyList<UserSummary> Users { get; set; } = [];

    [Parameter]
    public bool IsSubmitting { get; set; }

    [Parameter]
    public EventCallback<RaiseDemoAlertModel> OnSubmit { get; set; }

    private Task SubmitAsync() => OnSubmit.InvokeAsync(_model);
}
