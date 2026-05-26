using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Models;

[SugarTable("label_print_job_row")]
[SugarIndex(
    "ix_label_print_job_row_job_row",
    nameof(PrintJobId), OrderByType.Asc,
    nameof(RowIndex), OrderByType.Asc)]
[SugarIndex(
    "ix_label_print_job_row_import_row",
    nameof(ImportRowId), OrderByType.Asc)]
public class LabelPrintJobRow
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long PrintJobId { get; set; }

    public long ImportRowId { get; set; }

    public int RowIndex { get; set; }

    [SugarColumn(ColumnDataType = "TEXT")]
    public string RowDataJson { get; set; } = "{}";

    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? SearchText { get; set; }
}
