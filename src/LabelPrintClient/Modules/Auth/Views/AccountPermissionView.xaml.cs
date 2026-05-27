using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Auth.Models;
using LabelPrintClient.Modules.Auth.Services;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Auth.Views;

public partial class AccountPermissionView : System.Windows.Controls.UserControl
{
    private readonly List<AuthRole> _roles = new();
    private readonly List<AuthPermissionResource> _resources = new();
    private readonly Dictionary<string, System.Windows.Controls.CheckBox> _permissionBoxes = new(StringComparer.OrdinalIgnoreCase);

    public AccountPermissionView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAllAsync();
        Loaded += (_, _) => AppLanguageService.LanguageChanged += AppLanguageService_LanguageChanged;
        Unloaded += (_, _) => AppLanguageService.LanguageChanged -= AppLanguageService_LanguageChanged;
    }

    private AuthUser? SelectedUser => UserGrid.SelectedItem as AuthUser;

    private AuthRole? SelectedRole => RoleGrid.SelectedItem as AuthRole;

    private async Task RefreshAllAsync()
    {
        await PermissionBootstrapper.SyncAsync();
        await RefreshUsersAsync();
        await RefreshRolesAndPermissionsAsync();
    }

    private async Task RefreshUsersAsync()
    {
        var users = await AppDb.Db.Queryable<AuthUser>()
            .OrderBy(x => x.UserName)
            .ToListAsync();
        UserGrid.ItemsSource = users;
        if (users.Count > 0 && UserGrid.SelectedItem == null)
            UserGrid.SelectedIndex = 0;
    }

    private async Task RefreshRolesAndPermissionsAsync()
    {
        _roles.Clear();
        _roles.AddRange(await AppDb.Db.Queryable<AuthRole>()
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Name)
            .ToListAsync());
        RoleGrid.ItemsSource = _roles.ToList();

        _resources.Clear();
        _resources.AddRange(await AppDb.Db.Queryable<AuthPermissionResource>()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Sort)
            .ToListAsync());

        BuildPermissionList();
        BuildUserRoleList();
        if (_roles.Count > 0 && RoleGrid.SelectedItem == null)
            RoleGrid.SelectedIndex = 0;
    }

    private void BuildPermissionList()
    {
        _permissionBoxes.Clear();
        var stack = new StackPanel();
        var menus = _resources
            .Where(x => x.ResourceType == AuthPermissionResourceType.Menu)
            .OrderBy(x => x.Sort)
            .ToList();

        foreach (var menu in menus)
        {
            var menuBox = NewPermissionBox(menu, isMenu: true);
            stack.Children.Add(menuBox);

            foreach (var button in _resources
                         .Where(x => x.ResourceType == AuthPermissionResourceType.Button && x.ParentKey == menu.Key)
                         .OrderBy(x => x.Sort))
            {
                stack.Children.Add(NewPermissionBox(button, isMenu: false));
            }
        }

        PermissionList.Items.Clear();
        PermissionList.Items.Add(stack);
        _ = LoadSelectedRolePermissionsAsync();
    }

    private System.Windows.Controls.CheckBox NewPermissionBox(AuthPermissionResource resource, bool isMenu)
    {
        var box = new System.Windows.Controls.CheckBox
        {
            Content = $"{GetPermissionDisplayName(resource)}  [{resource.Key}]",
            Tag = resource.Key,
            Margin = isMenu ? new Thickness(0, 8, 0, 4) : new Thickness(22, 3, 0, 3),
            FontWeight = isMenu ? FontWeights.SemiBold : FontWeights.Normal
        };
        _permissionBoxes[resource.Key] = box;
        return box;
    }

    private void AppLanguageService_LanguageChanged(object? sender, LabelPrintClient.Config.AppLanguage language)
    {
        foreach (var resource in _resources)
        {
            if (_permissionBoxes.TryGetValue(resource.Key, out var box))
                box.Content = $"{GetPermissionDisplayName(resource)}  [{resource.Key}]";
        }
    }

    private static string GetPermissionDisplayName(AuthPermissionResource resource)
    {
        var key = $"Permission.{resource.Key}";
        var value = AppLanguageService.GetString(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? resource.Name : value;
    }

    private async void UserGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadSelectedUserRolesAsync();
    }

    private async void RoleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadSelectedRolePermissionsAsync();
    }

    private void BuildUserRoleList()
    {
        UserRoleList.Items.Clear();
        foreach (var role in _roles)
        {
            UserRoleList.Items.Add(new System.Windows.Controls.CheckBox
            {
                Content = role.Name,
                Tag = role.Id,
                Margin = new Thickness(4)
            });
        }
    }

    private async Task LoadSelectedUserRolesAsync()
    {
        foreach (var box in UserRoleList.Items.OfType<System.Windows.Controls.CheckBox>())
            box.IsChecked = false;

        var user = SelectedUser;
        if (user == null)
            return;

        var roleIds = await AppDb.Db.Queryable<AuthUserRole>()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.RoleId)
            .ToListAsync();
        var set = roleIds.ToHashSet();
        foreach (var box in UserRoleList.Items.OfType<System.Windows.Controls.CheckBox>())
        {
            if (box.Tag is long roleId)
                box.IsChecked = set.Contains(roleId);
        }
    }

    private async Task LoadSelectedRolePermissionsAsync()
    {
        foreach (var box in _permissionBoxes.Values)
            box.IsChecked = false;

        var role = SelectedRole;
        if (role == null)
            return;

        var keys = await AppDb.Db.Queryable<AuthRolePermission>()
            .Where(x => x.RoleId == role.Id)
            .Select(x => x.PermissionKey)
            .ToListAsync();
        var set = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, box) in _permissionBoxes)
            box.IsChecked = set.Contains(key);
    }

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountUserCreate, "新增用户")) return;

        var win = new UserEditWindow { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;

        var duplicate = await AppDb.Db.Queryable<AuthUser>()
            .Where(x => x.UserName == win.User.UserName)
            .AnyAsync();
        if (duplicate)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Account.UserNameExists"));
            return;
        }

        var hash = PasswordHasher.Hash(win.Password);
        win.User.Id = IdHelper.NewId();
        win.User.PasswordHash = hash.Hash;
        win.User.PasswordSalt = hash.Salt;
        win.User.PasswordIterations = hash.Iterations;
        win.User.CreateTime = DateTime.Now;
        await AppDb.Db.Insertable(win.User).ExecuteCommandAsync();
        await RefreshUsersAsync();
        AppMessageBox.Success(AppLanguageService.GetString("Account.UserCreated"));
    }

    private async void EditUser_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountUserEdit, "编辑用户")) return;
        var user = SelectedUser;
        if (user == null) return;

        var win = new UserEditWindow(user) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;

        await AppDb.Db.Updateable(win.User)
            .UpdateColumns(x => new { x.DisplayName, x.IsEnabled })
            .ExecuteCommandAsync();
        await RefreshUsersAsync();
        AppMessageBox.Success(AppLanguageService.GetString("Account.UserSaved"));
    }

    private async void ToggleUser_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountUserDisable, "启停用户")) return;
        var user = SelectedUser;
        if (user == null) return;

        if (CurrentUserService.Current?.UserId == user.Id && user.IsEnabled)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Account.CannotDisableCurrentUser"));
            return;
        }

        user.IsEnabled = !user.IsEnabled;
        await AppDb.Db.Updateable(user).UpdateColumns(x => new { x.IsEnabled }).ExecuteCommandAsync();
        await RefreshUsersAsync();
        AppMessageBox.Success(AppLanguageService.GetString(user.IsEnabled ? "Account.UserEnabled" : "Account.UserDisabled"));
    }

    private async void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountUserResetPassword, "重置用户密码")) return;
        var user = SelectedUser;
        if (user == null) return;

        var win = new ResetPasswordWindow { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;

        var hash = PasswordHasher.Hash(win.Password);
        user.PasswordHash = hash.Hash;
        user.PasswordSalt = hash.Salt;
        user.PasswordIterations = hash.Iterations;
        await AppDb.Db.Updateable(user)
            .UpdateColumns(x => new { x.PasswordHash, x.PasswordSalt, x.PasswordIterations })
            .ExecuteCommandAsync();
        AppMessageBox.Success(AppLanguageService.GetString("Account.PasswordReset"));
    }

    private async void AddRole_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountRoleCreate, "新增角色")) return;

        var win = new RoleEditWindow { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;

        var duplicate = await AppDb.Db.Queryable<AuthRole>()
            .Where(x => x.Code == win.Role.Code)
            .AnyAsync();
        if (duplicate)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Account.RoleCodeExists"));
            return;
        }

        win.Role.Id = IdHelper.NewId();
        win.Role.Sort = 100;
        win.Role.CreateTime = DateTime.Now;
        await AppDb.Db.Insertable(win.Role).ExecuteCommandAsync();
        await RefreshRolesAndPermissionsAsync();
        AppMessageBox.Success(AppLanguageService.GetString("Account.RoleCreated"));
    }

    private async void EditRole_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountRoleEdit, "编辑角色")) return;
        var role = SelectedRole;
        if (role == null) return;

        var win = new RoleEditWindow(role) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true) return;

        await AppDb.Db.Updateable(win.Role)
            .UpdateColumns(x => new { x.Name, x.IsEnabled })
            .ExecuteCommandAsync();
        await RefreshRolesAndPermissionsAsync();
        AppMessageBox.Success(AppLanguageService.GetString("Account.RoleSaved"));
    }

    private async void DeleteRole_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountRoleDelete, "删除角色")) return;
        var role = SelectedRole;
        if (role == null) return;
        if (role.IsSystem)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Account.SystemRoleCannotDelete"));
            return;
        }

        if (AppMessageBox.Show(AppLanguageService.Format("Account.DeleteRoleConfirm", role.Name), AppLanguageService.GetString("Common.Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Deleteable<AuthRolePermission>().Where(x => x.RoleId == role.Id).ExecuteCommandAsync();
            await AppDb.Db.Deleteable<AuthUserRole>().Where(x => x.RoleId == role.Id).ExecuteCommandAsync();
            await AppDb.Db.Deleteable<AuthRole>().Where(x => x.Id == role.Id).ExecuteCommandAsync();
        });
        await RefreshRolesAndPermissionsAsync();
        AppMessageBox.Success(AppLanguageService.GetString("Account.RoleDeleted"));
    }

    private async void SaveUserRoles_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountUserEdit, "保存用户角色")) return;
        var user = SelectedUser;
        if (user == null) return;

        var roleIds = UserRoleList.Items.OfType<System.Windows.Controls.CheckBox>()
            .Where(x => x.IsChecked == true && x.Tag is long)
            .Select(x => (long)x.Tag)
            .Distinct()
            .ToList();

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Deleteable<AuthUserRole>().Where(x => x.UserId == user.Id).ExecuteCommandAsync();
            if (roleIds.Count > 0)
            {
                var rows = roleIds.Select(roleId => new AuthUserRole
                {
                    Id = IdHelper.NewId(),
                    UserId = user.Id,
                    RoleId = roleId
                }).ToList();
                await AppDb.Db.Insertable(rows).ExecuteCommandAsync();
            }
        });
        AppMessageBox.Success(AppLanguageService.GetString("Account.UserRolesSaved"));
    }

    private async void SaveRolePermissions_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.AccountRolePermissions, "保存角色权限")) return;
        var role = SelectedRole;
        if (role == null) return;

        var keys = _permissionBoxes
            .Where(x => x.Value.IsChecked == true)
            .Select(x => x.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Deleteable<AuthRolePermission>().Where(x => x.RoleId == role.Id).ExecuteCommandAsync();
            if (keys.Count > 0)
            {
                var rows = keys.Select(key => new AuthRolePermission
                {
                    Id = IdHelper.NewId(),
                    RoleId = role.Id,
                    PermissionKey = key
                }).ToList();
                await AppDb.Db.Insertable(rows).ExecuteCommandAsync();
            }
        });

        await PermissionBootstrapper.GrantAdministratorAllPermissionsAsync();
        AppMessageBox.Success(AppLanguageService.GetString("Account.RolePermissionsSaved"));
    }
}
