using SqlSugar;

namespace LabelPrintClient.Modules.Template.Models;

[SugarTable("label_template")]
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

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdateTime { get; set; }

    public override string ToString() => Name;
}

