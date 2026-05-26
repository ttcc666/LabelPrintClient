using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Tests.Infrastructure;

public sealed class FakePrintExecutor : ILabelPrintExecutor
{
    public List<IReadOnlyList<Dictionary<string, string>>> Calls { get; } = new();

    public bool ThrowOnPrint { get; set; }

    public Task PrintAsync(
        LabelTemplate template,
        IReadOnlyList<LabelTemplateField> fields,
        IReadOnlyList<LabelPrintJobRow> jobRows,
        string? printerName,
        string startMessage,
        CancellationToken cancellationToken = default,
        IProgress<BackgroundTaskProgress>? progress = null)
    {
        if (ThrowOnPrint)
            throw new InvalidOperationException("fake print failure");

        var rows = jobRows
            .Select(x => JsonHelper.Deserialize<Dictionary<string, string>>(x.RowDataJson) ?? new Dictionary<string, string>())
            .ToList();
        Calls.Add(rows);
        progress?.Report(new BackgroundTaskProgress(jobRows.Count, jobRows.Count, startMessage));
        return Task.CompletedTask;
    }
}
