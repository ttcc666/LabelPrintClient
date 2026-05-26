using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Models;

[SugarTable("label_print_job")]
[SugarIndex(
    "ix_label_print_job_time",
    nameof(CreateTime), OrderByType.Desc,
    nameof(Id), OrderByType.Desc)]
[SugarIndex(
    "ix_label_print_job_status_time",
    nameof(Status), OrderByType.Asc,
    nameof(CreateTime), OrderByType.Desc)]
[SugarIndex(
    "ix_label_print_job_batch",
    nameof(BatchId), OrderByType.Asc)]
public class LabelPrintJob
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TemplateId { get; set; }

    public long BatchId { get; set; }

    [SugarColumn(Length = 100)]
    public string TemplateName { get; set; } = string.Empty;

    public int SelectedRowCount { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? PrinterName { get; set; }

    [SugarColumn(Length = 50)]
    public string Status { get; set; } = "Preview";

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? OperatorName { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = true)]
    public DateTime? PrintTime { get; set; }

    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? ErrorMessage { get; set; }
}
