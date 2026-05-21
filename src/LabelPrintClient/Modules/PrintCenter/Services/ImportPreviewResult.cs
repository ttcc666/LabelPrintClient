using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public sealed class ImportPreviewResult
{
    public long TemplateId { get; init; }

    public string TemplateName { get; init; } = string.Empty;

    public int TemplateVersion { get; init; }

    public string ExcelPath { get; init; } = string.Empty;

    public string ExcelFileName { get; init; } = string.Empty;

    public string? ExcelFileHash { get; init; }

    public IReadOnlyList<LabelTemplateField> Fields { get; init; } = Array.Empty<LabelTemplateField>();

    public IReadOnlyList<ImportRowDraft> Rows { get; init; } = Array.Empty<ImportRowDraft>();

    public int TotalRows => Rows.Count;

    public int ValidRows => Rows.Count(x => x.IsValid);

    public int InvalidRows => Rows.Count(x => !x.IsValid);
}
