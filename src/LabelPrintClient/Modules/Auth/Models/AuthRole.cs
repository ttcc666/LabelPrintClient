using SqlSugar;

namespace LabelPrintClient.Modules.Auth.Models;

[SugarTable("auth_role")]
[SugarIndex("ux_auth_role_code", nameof(Code), OrderByType.Asc, true)]
public class AuthRole
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string Name { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int Sort { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;
}
