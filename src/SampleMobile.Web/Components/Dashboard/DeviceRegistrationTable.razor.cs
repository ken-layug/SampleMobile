using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Web.Components.Dashboard;

public partial class DeviceRegistrationTable {
    [Parameter, EditorRequired]
    public IReadOnlyList<AdminDeviceRegistrationItem> Items { get; set; } = [];
}
