using SqlSugar;

namespace LabelPrintClient.Models;

[SugarTable("label_import_row")]
public class LabelImportRow
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long BatchId { get; set; }

    public long TemplateId { get; set; }

    public int RowIndex { get; set; }

    [SugarColumn(ColumnDataType = "TEXT")]
    public string RowDataJson { get; set; } = "{}";

    public bool IsValid { get; set; }

    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? ErrorMessage { get; set; }

    public bool IsPrinted { get; set; }

    public int PrintCount { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastPrintTime { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;
}
