using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Models;

[SugarTable("label_import_batch")]
public class LabelImportBatch
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TemplateId { get; set; }

    [SugarColumn(Length = 100)]
    public string TemplateName { get; set; } = string.Empty;

    public int TemplateVersion { get; set; }

    [SugarColumn(Length = 300)]
    public string ExcelFileName { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ExcelFileHash { get; set; }

    public int TotalRows { get; set; }

    public int ValidRows { get; set; }

    public int InvalidRows { get; set; }

    [SugarColumn(Length = 50)]
    public string Status { get; set; } = "Imported";

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? OperatorName { get; set; }

    public DateTime ImportTime { get; set; } = DateTime.Now;

    public override string ToString() => $"{ImportTime:yyyy-MM-dd HH:mm} {TemplateName} - {TotalRows}行";
}