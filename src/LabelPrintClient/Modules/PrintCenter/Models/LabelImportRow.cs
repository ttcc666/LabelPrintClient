using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Models;

[SugarTable("label_import_row")]
[SugarIndex(
    "ix_label_import_row_batch_row",
    nameof(BatchId), OrderByType.Asc,
    nameof(RowIndex), OrderByType.Asc)]
[SugarIndex(
    "ix_label_import_row_batch_valid",
    nameof(BatchId), OrderByType.Asc,
    nameof(IsValid), OrderByType.Asc)]
[SugarIndex(
    "ix_label_import_row_template",
    nameof(TemplateId), OrderByType.Asc)]
public class LabelImportRow
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long BatchId { get; set; }

    public long TemplateId { get; set; }

    public int RowIndex { get; set; }

    [SugarColumn(ColumnDataType = "TEXT")]
    public string RowDataJson { get; set; } = "{}";

    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? SearchText { get; set; }

    public bool IsValid { get; set; }

    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? ErrorMessage { get; set; }

    public bool IsPrinted { get; set; }

    public int PrintCount { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastPrintTime { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;
}
