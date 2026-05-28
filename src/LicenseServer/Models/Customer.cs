using SqlSugar;

namespace LicenseServer.Models;

[SugarTable("license_customer")]
public sealed class Customer
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 120)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? Contact { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Remark { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreateTime { get; set; } = DateTime.Now;
}
