using Microsoft.AspNetCore.Components;

namespace SampleMobile.Web.Components.Shared;

public partial class StatCard {
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public int Value { get; set; }

    [Parameter]
    public string? Hint { get; set; }

    /// <summary>Bootstrap contextual colour: primary, success, danger, warning, info, secondary.</summary>
    [Parameter]
    public string Tone { get; set; } = "secondary";
}
