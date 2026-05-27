using SqlSugar;

namespace LabelPrintClient.Modules.Auth.Models;

[SugarTable("auth_user")]
[SugarIndex("ux_auth_user_user_name", nameof(UserName), OrderByType.Asc, true)]
public class AuthUser
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(Length = 256)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(Length = 128)]
    public string PasswordSalt { get; set; } = string.Empty;

    public int PasswordIterations { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = true)]
    public DateTime? LastLoginTime { get; set; }
}
