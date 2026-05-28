namespace LabelPrintClient.Modules.Auth.Services;

public sealed class CurrentUserSession
{
    public long UserId { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<string> RoleCodes { get; init; } = Array.Empty<string>();

    public IReadOnlySet<string> PermissionKeys { get; init; } = new HashSet<string>();

    public string OperatorName => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;

    public bool HasPermission(string permissionKey) =>
        string.Equals(UserName, "System", System.StringComparison.OrdinalIgnoreCase) ||
        PermissionKeys.Contains(permissionKey);
}
