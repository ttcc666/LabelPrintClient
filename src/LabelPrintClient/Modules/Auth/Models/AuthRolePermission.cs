using SqlSugar;

namespace LabelPrintClient.Modules.Auth.Models;

[SugarTable("auth_role_permission")]
[SugarIndex("ux_auth_role_permission", nameof(RoleId), OrderByType.Asc, nameof(PermissionKey), OrderByType.Asc, true)]
public class AuthRolePermission
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long RoleId { get; set; }

    [SugarColumn(Length = 160)]
    public string PermissionKey { get; set; } = string.Empty;
}
