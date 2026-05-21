using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Models;

[SugarTable("label_print_job_row")]
public class LabelPrintJobRow
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long PrintJobId { get; set; }

    public long ImportRowId { get; set; }

    public int RowIndex { get; set; }

    [SugarColumn(ColumnDataType = "TEXT")]
    public string RowDataJson { get; set; } = "{}";
}

