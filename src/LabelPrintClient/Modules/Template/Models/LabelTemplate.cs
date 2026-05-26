using SqlSugar;

namespace LabelPrintClient.Modules.Template.Models;

[SugarTable("label_template")]
[SugarIndex(
    "ix_label_template_category_enabled_name",
    nameof(CategoryId), OrderByType.Asc,
    nameof(IsEnabled), OrderByType.Asc,
    nameof(Name), OrderByType.Asc)]
public class LabelTemplate
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long CategoryId { get; set; }

    [SugarColumn(Length = 100)]
    public string Name { get; set; } = string.Empty;

    public TemplateStorageType StorageType { get; set; } = TemplateStorageType.LocalFile;

    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? TemplatePath { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? TemplateFileName { get; set; }

    [SugarColumn(IsNullable = true)]
    public byte[]? TemplateContent { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? TemplateHash { get; set; }

    [SugarColumn(Length = 100)]
    public string DataSourceName { get; set; } = "LabelData";

    public int Version { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    [SugarColumn(IsNullable = true)]
    public bool? IsSerialNumber { get; set; } = false;

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? SerialNumberPrefix { get; set; } = "SN-";

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? SerialNumberPattern { get; set; } = "SN-{seq:0000}";

    public SerialResetPeriod SerialResetPeriod { get; set; } = SerialResetPeriod.Never;

    [SugarColumn(IsNullable = true)]
    public long? CurrentSerialValue { get; set; } = 0;

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdateTime { get; set; }

    public override string ToString() => Name;
}
