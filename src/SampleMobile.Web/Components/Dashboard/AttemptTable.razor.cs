using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Repositories;

namespace SampleMobile.Web.Components.Dashboard;

public partial class AttemptTable {
    [Parameter, EditorRequired]
    public IReadOnlyList<DeliveryAttemptItem> Attempts { get; set; } = [];
}
