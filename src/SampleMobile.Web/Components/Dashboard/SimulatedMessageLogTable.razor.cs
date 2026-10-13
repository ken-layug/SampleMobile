using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Push;

namespace SampleMobile.Web.Components.Dashboard;

public partial class SimulatedMessageLogTable {
    [Parameter, EditorRequired]
    public IReadOnlyList<SimulatedPushRecord> Messages { get; set; } = [];
}
