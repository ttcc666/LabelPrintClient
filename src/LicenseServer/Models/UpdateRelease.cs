using SqlSugar;

namespace LicenseServer.Models;

[SugarTable("license_update_release")]
[SugarIndex("ix_update_release_lookup", nameof(ProductCode), OrderByType.Asc, nameof(Channel), OrderByType.Asc, nameof(IsEnabled), OrderByType.Asc)]
public sealed class UpdateRelease
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 80)]
    public string ProductCode { get; set; } = "LABEL_PRINT_CLIENT";

    [SugarColumn(Length = 40)]
    public string Channel { get; set; } = "stable";

    [SugarColumn(Length = 40)]
    public string Version { get; set; } = "1.0.0";

    [SugarColumn(Length = 2000, IsNullable = true)]
    public string? ReleaseNotes { get; set; }

    public bool IsMandatory { get; set; }

    public bool IsEnabled { get; set; } = true;

    [SugarColumn(Length = 260)]
    public string PackageDirectory { get; set; } = string.Empty;

    [SugarColumn(Length = 120)]
    public string FeedFileName { get; set; } = "RELEASES";

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? SetupFileName { get; set; }

    [SugarColumn(Length = 128)]
    public string Sha256 { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;

    public DateTime UpdateTime { get; set; } = DateTime.Now;
}
