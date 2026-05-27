using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Auth.Models;
using LabelPrintClient.Modules.Auth.Services;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.Auth;

public class RbacFunctionalTests
{
    [Fact]
    public async Task SyncAsync_SeedsPermissionsAndAdministratorHasAllPermissions()
    {
        using var database = TestDatabase.Create();

        await PermissionBootstrapper.SyncAsync();

        var resources = await AppDb.Db.Queryable<AuthPermissionResource>().ToListAsync();
        var admin = await AppDb.Db.Queryable<AuthRole>().FirstAsync(x => x.Code == AuthRoleCodes.Administrator);
        var adminKeys = await AppDb.Db.Queryable<AuthRolePermission>()
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.PermissionKey)
            .ToListAsync();

        Assert.NotEmpty(resources);
        Assert.Contains(resources, x => x.Key == Permissions.MenuPrintCenter);
        Assert.Contains(resources, x => x.Key == Permissions.PrintCenterPrint);
        Assert.True(resources.Where(x => x.IsEnabled).All(x => adminKeys.Contains(x.Key)));
    }

    [Fact]
    public async Task CreateInitialAdministratorAsync_CreatesAdminUserWithHashedPassword()
    {
        using var database = TestDatabase.Create();

        var user = await PermissionBootstrapper.CreateInitialAdministratorAsync("admin", "管理员", "secret123");

        Assert.True(await PermissionBootstrapper.HasAnyUserAsync());
        Assert.True(await PermissionBootstrapper.HasAdministratorUserAsync());
        Assert.NotEqual("secret123", user.PasswordHash);
        Assert.True(PasswordHasher.Verify("secret123", user.PasswordHash, user.PasswordSalt, user.PasswordIterations));
        Assert.False(PasswordHasher.Verify("bad-secret", user.PasswordHash, user.PasswordSalt, user.PasswordIterations));

        var session = await AuthService.LoginAsync("admin", "secret123");
        Assert.Equal("管理员", session.OperatorName);
        Assert.Contains(Permissions.MenuAccountPermission, session.PermissionKeys);
    }

    [Fact]
    public async Task HasAdministratorUserAsync_ReturnsFalse_WhenOnlyNonAdminUsersExist()
    {
        using var database = TestDatabase.Create();
        await PermissionBootstrapper.SyncAsync();

        var password = PasswordHasher.Hash("secret123");
        var user = new AuthUser
        {
            Id = IdHelper.NewId(),
            UserName = "operator",
            DisplayName = "操作员",
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            PasswordIterations = password.Iterations,
            IsEnabled = true
        };
        var operatorRole = await AppDb.Db.Queryable<AuthRole>().FirstAsync(x => x.Code == AuthRoleCodes.Operator);

        await AppDb.Db.Insertable(user).ExecuteCommandAsync();
        await AppDb.Db.Insertable(new AuthUserRole
        {
            Id = IdHelper.NewId(),
            UserId = user.Id,
            RoleId = operatorRole.Id
        }).ExecuteCommandAsync();

        Assert.True(await PermissionBootstrapper.HasAnyUserAsync());
        Assert.False(await PermissionBootstrapper.HasAdministratorUserAsync());
    }

    [Fact]
    public async Task LoginAsync_DeniesDisabledUser()
    {
        using var database = TestDatabase.Create();
        await PermissionBootstrapper.SyncAsync();

        var password = PasswordHasher.Hash("secret123");
        await AppDb.Db.Insertable(new AuthUser
        {
            Id = IdHelper.NewId(),
            UserName = "disabled",
            DisplayName = "Disabled",
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            PasswordIterations = password.Iterations,
            IsEnabled = false
        }).ExecuteCommandAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => AuthService.LoginAsync("disabled", "secret123"));
    }

    [Fact]
    public async Task LoginAsync_CombinesRolePermissions()
    {
        using var database = TestDatabase.Create();
        await PermissionBootstrapper.SyncAsync();

        var role = new AuthRole
        {
            Id = IdHelper.NewId(),
            Code = "Limited",
            Name = "受限角色",
            IsEnabled = true
        };
        var password = PasswordHasher.Hash("secret123");
        var user = new AuthUser
        {
            Id = IdHelper.NewId(),
            UserName = "limited",
            DisplayName = "受限用户",
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            PasswordIterations = password.Iterations,
            IsEnabled = true
        };

        await AppDb.Db.Insertable(role).ExecuteCommandAsync();
        await AppDb.Db.Insertable(user).ExecuteCommandAsync();
        await AppDb.Db.Insertable(new AuthUserRole { Id = IdHelper.NewId(), UserId = user.Id, RoleId = role.Id }).ExecuteCommandAsync();
        await AppDb.Db.Insertable(new AuthRolePermission { Id = IdHelper.NewId(), RoleId = role.Id, PermissionKey = Permissions.MenuPrintCenter }).ExecuteCommandAsync();

        var session = await AuthService.LoginAsync("limited", "secret123");

        Assert.Contains(Permissions.MenuPrintCenter, session.PermissionKeys);
        Assert.DoesNotContain(Permissions.MenuTemplateManage, session.PermissionKeys);
    }

    [Fact]
    public async Task CurrentUserService_OperatorName_UsesCurrentLoginUser()
    {
        using var database = TestDatabase.Create();
        await PermissionBootstrapper.CreateInitialAdministratorAsync("admin", "管理员", "secret123");

        await AuthService.LoginAsync("admin", "secret123");

        Assert.Equal("管理员", CurrentUserService.OperatorName);
    }
}
