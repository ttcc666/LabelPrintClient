namespace LabelPrintClient.Modules.PrintCenter.ViewModels;

public sealed class ImportPreviewRowGridItem
{
    public int RowIndex { get; set; }

    public bool IsValid { get; set; }

    public string? ErrorMessage { get; set; }

    public Dictionary<string, string> Data { get; set; } = new();
}
