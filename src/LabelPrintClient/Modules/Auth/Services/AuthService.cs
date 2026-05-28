using LabelPrintClient.Database;
using LabelPrintClient.Modules.Auth.Models;

namespace LabelPrintClient.Modules.Auth.Services;

public static class AuthService
{
    public static async Task<CurrentUserSession> LoginAsync(string userName, string password)
    {
        var normalizedUserName = userName.Trim();
        var users = await AppDb.Db.Queryable<AuthUser>()
            .Where(x => x.UserName == normalizedUserName)
            .ToListAsync()
            .ConfigureAwait(false);
        var user = users.FirstOrDefault(x => string.Equals(x.UserName, normalizedUserName, StringComparison.OrdinalIgnoreCase));
        if (user == null)
            throw new InvalidOperationException("用户名或密码错误。");

        if (!user.IsEnabled)
            throw new InvalidOperationException("该用户已被禁用。");

        if (!PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
            throw new InvalidOperationException("用户名或密码错误。");

        var session = await BuildSessionAsync(user).ConfigureAwait(false);
        user.LastLoginTime = DateTime.Now;
        await AppDb.Db.Updateable(user)
            .UpdateColumns(x => new { x.LastLoginTime })
            .ExecuteCommandAsync()
            .ConfigureAwait(false);
        CurrentUserService.SignIn(session);
        return session;
    }

    public static async Task<CurrentUserSession> BuildSessionAsync(AuthUser user)
    {
        System.Collections.Generic.List<string> roleCodes;
        System.Collections.Generic.HashSet<string> permissionKeys;

        if (string.Equals(user.UserName, "System", StringComparison.OrdinalIgnoreCase))
        {
            roleCodes = new System.Collections.Generic.List<string> { AuthRoleCodes.Administrator };
            permissionKeys = Permissions.All.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var userRoles = await AppDb.Db.Queryable<AuthUserRole>()
                .Where(x => x.UserId == user.Id)
                .ToListAsync()
                .ConfigureAwait(false);
            var roleIds = userRoles.Select(x => x.RoleId).Distinct().ToList();

            var roles = roleIds.Count == 0
                ? new System.Collections.Generic.List<AuthRole>()
                : await AppDb.Db.Queryable<AuthRole>()
                    .Where(x => roleIds.Contains(x.Id) && x.IsEnabled)
                    .ToListAsync()
                    .ConfigureAwait(false);
            var enabledRoleIds = roles.Select(x => x.Id).ToList();

            var permissions = enabledRoleIds.Count == 0
                ? new System.Collections.Generic.List<string>()
                : await AppDb.Db.Queryable<AuthRolePermission>()
                    .Where(x => enabledRoleIds.Contains(x.RoleId))
                    .Select(x => x.PermissionKey)
                    .ToListAsync()
                    .ConfigureAwait(false);

            roleCodes = roles.Select(x => x.Code).ToList();
            permissionKeys = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new CurrentUserSession
        {
            UserId = user.Id,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            RoleCodes = roleCodes,
            PermissionKeys = permissionKeys
        };
    }

    public static void Logout() => CurrentUserService.SignOut();
}
