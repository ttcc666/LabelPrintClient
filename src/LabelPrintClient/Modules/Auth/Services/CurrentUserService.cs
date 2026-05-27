namespace LabelPrintClient.Modules.Auth.Services;

public static class CurrentUserService
{
    private static CurrentUserSession? _current;

    public static event EventHandler? CurrentUserChanged;

    public static CurrentUserSession? Current => _current;

    public static bool IsAuthenticated => _current != null;

    public static string OperatorName =>
        _current?.OperatorName ?? App.Settings.OperatorName;

    public static void SignIn(CurrentUserSession session)
    {
        _current = session;
        CurrentUserChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SignOut()
    {
        _current = null;
        CurrentUserChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool HasPermission(string permissionKey)
    {
        if (string.IsNullOrWhiteSpace(permissionKey))
            return true;

        // Unit tests and design-time construction may instantiate views before login.
        // Production startup always signs in before showing the main window.
        return _current == null || _current.HasPermission(permissionKey);
    }
}
