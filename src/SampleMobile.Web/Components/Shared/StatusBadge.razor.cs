using Microsoft.AspNetCore.Components;

namespace SampleMobile.Web.Components.Shared;

public partial class StatusBadge {
    [Parameter, EditorRequired]
    public BadgeDisplay Badge { get; set; } = null!;
}
