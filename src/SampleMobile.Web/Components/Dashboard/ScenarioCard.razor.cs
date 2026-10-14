using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Features.Simulation;

namespace SampleMobile.Web.Components.Dashboard;

public partial class ScenarioCard {
    [Parameter, EditorRequired]
    public SimulatedScenario Scenario { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Description { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Expected { get; set; } = string.Empty;

    [Parameter]
    public bool IsRunning { get; set; }

    [Parameter]
    public EventCallback<SimulatedScenario> OnRun { get; set; }
}
