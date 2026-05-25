using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Models;

[SugarTable("label_serial_counter")]
[SugarIndex(
    "ux_label_serial_counter_scope",
    nameof(TemplateId), OrderByType.Asc,
    nameof(ImportRowId), OrderByType.Asc,
    nameof(CounterKey), OrderByType.Asc,
    true)]
public class LabelSerialCounter
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TemplateId { get; set; }

    public long ImportRowId { get; set; }

    [SugarColumn(Length = 20)]
    public string CounterKey { get; set; } = "global";

    public long CurrentValue { get; set; }

    public DateTime UpdateTime { get; set; } = DateTime.Now;
}
