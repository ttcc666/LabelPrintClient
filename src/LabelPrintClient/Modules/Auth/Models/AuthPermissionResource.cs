using SqlSugar;

namespace LabelPrintClient.Modules.Auth.Models;

[SugarTable("auth_permission_resource")]
[SugarIndex("ux_auth_permission_key", nameof(Key), OrderByType.Asc, true)]
public class AuthPermissionResource
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 160)]
    public string Key { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string Name { get; set; } = string.Empty;

    public AuthPermissionResourceType ResourceType { get; set; }

    [SugarColumn(Length = 160, IsNullable = true)]
    public string? ParentKey { get; set; }

    public int Sort { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreateTime { get; set; } = DateTime.Now;
}
