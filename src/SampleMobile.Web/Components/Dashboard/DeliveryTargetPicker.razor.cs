using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Web.Components.Dashboard;

public partial class DeliveryTargetPicker {
    [Parameter, EditorRequired]
    public IReadOnlyList<DeliveryTargetOption> Targets { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlySet<Guid> SelectedIds { get; set; } = new HashSet<Guid>();

    [Parameter]
    public EventCallback<Guid> OnToggle { get; set; }
}
