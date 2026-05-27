using System.Windows;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Auth.Services;

public static class AuthorizationService
{
    public static bool EnsurePermission(string permissionKey, string? actionName = null)
    {
        if (CurrentUserService.HasPermission(permissionKey))
            return true;

        var name = string.IsNullOrWhiteSpace(actionName) ? "当前操作" : actionName;
        AppMessageBox.Show($"无权限执行：{name}", "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
