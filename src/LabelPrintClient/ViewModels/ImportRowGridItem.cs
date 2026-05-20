using LabelPrintClient.Infrastructure;

namespace LabelPrintClient.ViewModels;

public class ImportRowGridItem : NotifyObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public long Id { get; set; }

    public int RowIndex { get; set; }

    public bool IsValid { get; set; }

    public bool IsPrinted { get; set; }

    public int PrintCount { get; set; }

    public string? ErrorMessage { get; set; }

    public Dictionary<string, string> Data { get; set; } = new();
}
