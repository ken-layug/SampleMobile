using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Web.Components.Dashboard;

public partial class InboxTable {
    [Parameter, EditorRequired]
    public IReadOnlyList<NotificationResponse> Items { get; set; } = [];
}
