using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using SampleMobile.Application.Common;
using SampleMobile.Infrastructure.Identity;

namespace SampleMobile.Web.Components.Account;

/// <summary>
/// Static SSR sign-in for the dashboard. Signing in sets the Identity cookie, so it must run with an
/// HTTP response available — which is why this page is not interactive.
/// </summary>
public partial class Login {
    [Inject]
    private SignInManager<AppUser> SignInManager { get; set; } = null!;

    [Inject]
    private UserManager<AppUser> UserManager { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private ILogger<Login> Logger { get; set; } = null!;

    [SupplyParameterFromForm]
    private LoginInput Input { get; set; } = null!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    private string? ErrorMessage { get; set; }

    protected override void OnInitialized() {
        Input ??= new LoginInput();
    }

    private async Task SignInAsync() {
        var user = await UserManager.FindByNameAsync(Input.UserName!.Trim());

        // Mobile demo users exist too; only administrators may open the dashboard. The check runs before
        // signing in so a non-admin never receives a dashboard cookie.
        if (user is null || !await UserManager.IsInRoleAsync(user, Roles.Admin)) {
            ErrorMessage = "Invalid user name or password, or the account is not an administrator.";
            return;
        }

        var result = await SignInManager.PasswordSignInAsync(user, Input.Password!, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded) {
            ErrorMessage = result.IsLockedOut
                ? "The account is temporarily locked after too many failed attempts."
                : "Invalid user name or password, or the account is not an administrator.";
            return;
        }

        Logger.LogInformation("Administrator {UserName} signed in to the dashboard", user.UserName);
        Navigation.NavigateTo(SafeReturnUrl(ReturnUrl), forceLoad: true);
    }

    // Only local paths, so the login page can't be used as an open redirect.
    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";

    private sealed class LoginInput {
        [Required]
        [Display(Name = "User name")]
        public string? UserName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }
}
