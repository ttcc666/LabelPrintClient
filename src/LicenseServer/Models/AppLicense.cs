using SqlSugar;

namespace LicenseServer.Models;

[SugarTable("license_license")]
[SugarIndex("ix_license_access_key_hash", nameof(AccessKeyHash), OrderByType.Asc)]
public sealed class AppLicense
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long CustomerId { get; set; }

    [SugarColumn(Length = 80)]
    public string ProductCode { get; set; } = "LABEL_PRINT_CLIENT";

    public LicenseMode LicenseMode { get; set; }

    public DateTime ExpireTime { get; set; }

    public bool IsEnabled { get; set; } = true;

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? MachineCode { get; set; }

    public int TotalCount { get; set; } = 1;

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? AccessKeyHash { get; set; }

    [SugarColumn(Length = 120)]
    public string IssuedTo { get; set; } = string.Empty;

    public DateTime CreateTime { get; set; } = DateTime.Now;
}
