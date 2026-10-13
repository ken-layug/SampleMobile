using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SampleMobile.Web.Components.Account;

/// <summary>
/// Rendered by the router when a page's policy fails. Signed-out users go to the login page;
/// signed-in users without the Admin role get the access-denied page.
/// </summary>
public sealed class RedirectToLogin : ComponentBase {
    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnInitializedAsync() {
        var state = await AuthenticationState;
        if (state.User.Identity?.IsAuthenticated == true) {
            Navigation.NavigateTo("account/access-denied", forceLoad: true);
            return;
        }

        var returnUrl = Uri.EscapeDataString("/" + Navigation.ToBaseRelativePath(Navigation.Uri));
        Navigation.NavigateTo($"account/login?returnUrl={returnUrl}", forceLoad: true);
    }
}
