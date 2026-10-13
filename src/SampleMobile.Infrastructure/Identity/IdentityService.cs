using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Infrastructure.Persistence;

namespace SampleMobile.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    AppDbContext db) : IIdentityService {

    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string userName, string password, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password)) {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName.Trim());
        if (user is null) {
            return null;
        }

        // Counts failures towards lockout, so the endpoint can't be used for unlimited password guessing.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded) {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new AuthenticatedUser(user.Id, user.UserName ?? userName, roles.ToList());
    }

    public async Task<UserSummary?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default) {
        var users = await QueryUsersAsync(user => user.Id == userId, cancellationToken);
        return users.SingleOrDefault();
    }

    public Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken = default) =>
        QueryUsersAsync(_ => true, cancellationToken);

    private async Task<IReadOnlyList<UserSummary>> QueryUsersAsync(
        System.Linq.Expressions.Expression<Func<AppUser, bool>> filter, CancellationToken cancellationToken) {
        var users = await db.Users.AsNoTracking()
            .Where(filter)
            .OrderBy(user => user.UserName)
            .Select(user => new { user.Id, user.UserName, user.DisplayName })
            .ToListAsync(cancellationToken);
        if (users.Count == 0) {
            return [];
        }

        var userIds = users.Select(user => user.Id).ToList();
        var roles = await (
                from userRole in db.UserRoles.AsNoTracking()
                where userIds.Contains(userRole.UserId)
                join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        return users
            .Select(user => new UserSummary(
                user.Id,
                user.UserName ?? string.Empty,
                user.DisplayName,
                roles.Where(role => role.UserId == user.Id && role.Name is not null).Select(role => role.Name!).ToList()))
            .ToList();
    }
}
