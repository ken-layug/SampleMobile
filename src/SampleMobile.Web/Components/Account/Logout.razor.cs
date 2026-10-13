using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using SampleMobile.Infrastructure.Identity;

namespace SampleMobile.Web.Components.Account;

/// <summary>Static SSR sign-out. Only a POST (with antiforgery token) signs out; a GET just shows the button.</summary>
public partial class Logout {
    [Inject]
    private SignInManager<AppUser> SignInManager { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    private async Task SignOutAsync() {
        await SignInManager.SignOutAsync();
        Navigation.NavigateTo("account/login", forceLoad: true);
    }
}
