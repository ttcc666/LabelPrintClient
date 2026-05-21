namespace LabelPrintClient.Modules.PrintCenter.Services;

public class ImportRowDraft
{
    public int RowIndex { get; set; }

    public Dictionary<string, string> Data { get; set; } = new();

    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; set; } = new();

    public string ErrorMessage => string.Join("；", Errors);
}
