using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Push;

namespace SampleMobile.Web.Components.Dashboard;

public partial class ProviderStatusList {
    [Parameter, EditorRequired]
    public IReadOnlyList<PushProviderDescriptor> Providers { get; set; } = [];
}
