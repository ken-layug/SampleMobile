using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Repositories;

namespace SampleMobile.Web.Components.Dashboard;

public partial class DeliveryHistoryTable {
    [Parameter, EditorRequired]
    public IReadOnlyList<DeliveryJobSummary> Items { get; set; } = [];
}
