using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Auth.Models;

namespace LabelPrintClient.Modules.Auth.Services;

public static class PermissionBootstrapper
{
    public static async Task SyncAsync()
    {
        await EnsureDefaultRolesAsync().ConfigureAwait(false);
        await SyncResourcesAsync().ConfigureAwait(false);
        await GrantDefaultRolePermissionsAsync().ConfigureAwait(false);
        await GrantAdministratorAllPermissionsAsync().ConfigureAwait(false);
    }

    public static async Task<bool> HasAnyUserAsync()
    {
        return await AppDb.Db.Queryable<AuthUser>().AnyAsync().ConfigureAwait(false);
    }

    public static async Task<bool> HasAdministratorUserAsync()
    {
        var adminRole = await GetRoleByCodeAsync(AuthRoleCodes.Administrator).ConfigureAwait(false);
        if (adminRole == null)
            return false;

        var adminUserIds = await AppDb.Db.Queryable<AuthUserRole>()
            .Where(x => x.RoleId == adminRole.Id)
            .Select(x => x.UserId)
            .ToListAsync()
            .ConfigureAwait(false);
        if (adminUserIds.Count == 0)
            return false;

        return await AppDb.Db.Queryable<AuthUser>()
            .Where(x => adminUserIds.Contains(x.Id) && x.IsEnabled)
            .AnyAsync()
            .ConfigureAwait(false);
    }

    public static async Task<AuthUser> CreateInitialAdministratorAsync(string userName, string displayName, string password)
    {
        await SyncAsync().ConfigureAwait(false);
        if (await HasAdministratorUserAsync().ConfigureAwait(false))
            throw new InvalidOperationException("系统已经存在管理员用户，不能重复初始化管理员。");

        var normalizedUserName = userName.Trim();
        var duplicateUserName = await AppDb.Db.Queryable<AuthUser>()
            .Where(x => x.UserName == normalizedUserName)
            .AnyAsync()
            .ConfigureAwait(false);
        if (duplicateUserName)
            throw new InvalidOperationException("用户名已存在，请换一个管理员用户名。");

        var adminRole = await GetRoleByCodeAsync(AuthRoleCodes.Administrator).ConfigureAwait(false)
            ?? throw new InvalidOperationException("管理员角色初始化失败。");

        var passwordHash = PasswordHasher.Hash(password);
        var user = new AuthUser
        {
            Id = IdHelper.NewId(),
            UserName = normalizedUserName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUserName : displayName.Trim(),
            PasswordHash = passwordHash.Hash,
            PasswordSalt = passwordHash.Salt,
            PasswordIterations = passwordHash.Iterations,
            IsEnabled = true,
            CreateTime = DateTime.Now
        };

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Insertable(user).ExecuteCommandAsync().ConfigureAwait(false);
            await AppDb.Db.Insertable(new AuthUserRole
            {
                Id = IdHelper.NewId(),
                UserId = user.Id,
                RoleId = adminRole.Id
            }).ExecuteCommandAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        return user;
    }

    private static async Task EnsureDefaultRolesAsync()
    {
        var roles = await AppDb.Db.Queryable<AuthRole>().ToListAsync().ConfigureAwait(false);
        var existing = roles.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        await EnsureRoleAsync(existing, AuthRoleCodes.Administrator, "管理员", true, 10).ConfigureAwait(false);
        await EnsureRoleAsync(existing, AuthRoleCodes.Supervisor, "主管", true, 20).ConfigureAwait(false);
        await EnsureRoleAsync(existing, AuthRoleCodes.Operator, "操作员", true, 30).ConfigureAwait(false);
    }

    private static async Task EnsureRoleAsync(
        Dictionary<string, AuthRole> existing,
        string code,
        string name,
        bool isSystem,
        int sort)
    {
        if (existing.TryGetValue(code, out var role))
        {
            role.Name = name;
            role.IsSystem = isSystem;
            role.Sort = sort;
            await AppDb.Db.Updateable(role).ExecuteCommandAsync().ConfigureAwait(false);
            return;
        }

        await AppDb.Db.Insertable(new AuthRole
        {
            Id = IdHelper.NewId(),
            Code = code,
            Name = name,
            IsSystem = isSystem,
            IsEnabled = true,
            Sort = sort,
            CreateTime = DateTime.Now
        }).ExecuteCommandAsync().ConfigureAwait(false);
    }

    private static async Task SyncResourcesAsync()
    {
        var resources = await AppDb.Db.Queryable<AuthPermissionResource>().ToListAsync().ConfigureAwait(false);
        var existing = resources.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Permissions.All)
        {
            if (existing.TryGetValue(definition.Key, out var resource))
            {
                resource.Name = definition.Name;
                resource.ResourceType = definition.ResourceType;
                resource.ParentKey = definition.ParentKey;
                resource.Sort = definition.Sort;
                resource.IsEnabled = true;
                await AppDb.Db.Updateable(resource).ExecuteCommandAsync().ConfigureAwait(false);
                continue;
            }

            await AppDb.Db.Insertable(new AuthPermissionResource
            {
                Id = IdHelper.NewId(),
                Key = definition.Key,
                Name = definition.Name,
                ResourceType = definition.ResourceType,
                ParentKey = definition.ParentKey,
                Sort = definition.Sort,
                IsEnabled = true,
                CreateTime = DateTime.Now
            }).ExecuteCommandAsync().ConfigureAwait(false);
        }
    }

    public static async Task GrantAdministratorAllPermissionsAsync()
    {
        var adminRole = await GetRoleByCodeAsync(AuthRoleCodes.Administrator).ConfigureAwait(false);
        if (adminRole == null)
            return;

        var enabledKeys = await AppDb.Db.Queryable<AuthPermissionResource>()
            .Where(x => x.IsEnabled)
            .Select(x => x.Key)
            .ToListAsync()
            .ConfigureAwait(false);
        var granted = await AppDb.Db.Queryable<AuthRolePermission>()
            .Where(x => x.RoleId == adminRole.Id)
            .Select(x => x.PermissionKey)
            .ToListAsync()
            .ConfigureAwait(false);
        var grantedSet = granted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = enabledKeys
            .Where(x => !grantedSet.Contains(x))
            .Select(x => new AuthRolePermission
            {
                Id = IdHelper.NewId(),
                RoleId = adminRole.Id,
                PermissionKey = x
            })
            .ToList();

        if (missing.Count > 0)
            await AppDb.Db.Insertable(missing).ExecuteCommandAsync().ConfigureAwait(false);
    }

    private static async Task GrantDefaultRolePermissionsAsync()
    {
        await GrantMissingAsync(AuthRoleCodes.Operator, new[]
        {
            Permissions.MenuPrintCenter,
            Permissions.PrintCenterRefresh,
            Permissions.PrintCenterDownloadExcel,
            Permissions.PrintCenterImportExcel,
            Permissions.PrintCenterBatchSearch,
            Permissions.PrintCenterLoadBatches,
            Permissions.PrintCenterRowSearch,
            Permissions.PrintCenterSelectRows,
            Permissions.PrintCenterPreview,
            Permissions.PrintCenterPrint
        }).ConfigureAwait(false);

        await GrantMissingAsync(AuthRoleCodes.Supervisor, new[]
        {
            Permissions.MenuPrintCenter,
            Permissions.MenuPrintHistory,
            Permissions.MenuTaskCenter,
            Permissions.PrintCenterRefresh,
            Permissions.PrintCenterBatchSearch,
            Permissions.PrintCenterLoadBatches,
            Permissions.PrintCenterVoidBatch,
            Permissions.PrintCenterRowSearch,
            Permissions.PrintCenterPreview,
            Permissions.PrintCenterPrint,
            Permissions.PrintCenterReprint,
            Permissions.PrintHistoryRefresh,
            Permissions.PrintHistorySearch,
            Permissions.PrintHistoryRetry,
            Permissions.PrintHistoryReprint,
            Permissions.TaskCenterSearch,
            Permissions.TaskCenterClearCompleted
        }).ConfigureAwait(false);
    }

    private static async Task GrantMissingAsync(string roleCode, IEnumerable<string> permissionKeys)
    {
        var role = await GetRoleByCodeAsync(roleCode).ConfigureAwait(false);
        if (role == null)
            return;

        var existing = await AppDb.Db.Queryable<AuthRolePermission>()
            .Where(x => x.RoleId == role.Id)
            .Select(x => x.PermissionKey)
            .ToListAsync()
            .ConfigureAwait(false);
        if (existing.Count > 0)
            return;

        var set = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = permissionKeys
            .Where(x => !set.Contains(x))
            .Select(x => new AuthRolePermission
            {
                Id = IdHelper.NewId(),
                RoleId = role.Id,
                PermissionKey = x
            })
            .ToList();

        if (rows.Count > 0)
            await AppDb.Db.Insertable(rows).ExecuteCommandAsync().ConfigureAwait(false);
    }

    private static async Task<AuthRole?> GetRoleByCodeAsync(string code)
    {
        var roles = await AppDb.Db.Queryable<AuthRole>()
            .Where(x => x.Code == code)
            .ToListAsync()
            .ConfigureAwait(false);
        return roles.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}
