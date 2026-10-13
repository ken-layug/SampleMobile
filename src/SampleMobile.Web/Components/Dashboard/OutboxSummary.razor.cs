using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Repositories;

namespace SampleMobile.Web.Components.Dashboard;

public partial class OutboxSummary {
    [Parameter, EditorRequired]
    public DashboardStats Stats { get; set; } = null!;
}
