using Microsoft.AspNetCore.Identity;

namespace SampleMobile.Infrastructure.Identity;

/// <summary>The ASP.NET Core Identity account. Use cases only ever see it through <c>IIdentityService</c>.</summary>
public sealed class AppUser : IdentityUser<Guid> {
    public AppUser() {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string? DisplayName { get; set; }
}
