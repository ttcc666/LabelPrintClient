using SqlSugar;

namespace LabelPrintClient.Modules.Auth.Models;

[SugarTable("auth_user_role")]
[SugarIndex("ux_auth_user_role", nameof(UserId), OrderByType.Asc, nameof(RoleId), OrderByType.Asc, true)]
public class AuthUserRole
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long UserId { get; set; }

    public long RoleId { get; set; }
}
