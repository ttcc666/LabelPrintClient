using System.Windows;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Auth.Services;

public static class AuthorizationService
{
    public static bool EnsurePermission(string permissionKey, string? actionName = null)
    {
        if (CurrentUserService.HasPermission(permissionKey))
            return true;

        string name;
        if (!string.IsNullOrWhiteSpace(actionName))
        {
            var key = $"Permission.{permissionKey}";
            var translated = AppLanguageService.GetString(key);
            name = string.Equals(translated, key, StringComparison.Ordinal) ? actionName : translated;
        }
        else
        {
            name = AppLanguageService.GetString("Common.CurrentOperation");
        }

        AppMessageBox.Show(
            AppLanguageService.Format("Common.PermissionDenied", name),
            AppLanguageService.GetString("Common.PermissionDeniedTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }
}
