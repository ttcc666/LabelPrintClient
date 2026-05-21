namespace LabelPrintClient.Modules.PrintCenter.ViewModels;

public class PrintJobRowGridItem
{
    public long Id { get; set; }

    public long ImportRowId { get; set; }

    public int RowIndex { get; set; }

    public Dictionary<string, string> Data { get; set; } = new();
}
