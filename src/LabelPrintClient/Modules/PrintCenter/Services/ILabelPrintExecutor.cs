using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public interface ILabelPrintExecutor
{
    Task PrintAsync(
        LabelTemplate template,
        IReadOnlyList<LabelTemplateField> fields,
        IReadOnlyList<LabelPrintJobRow> jobRows,
        string? printerName,
        string startMessage,
        CancellationToken cancellationToken = default,
        IProgress<BackgroundTaskProgress>? progress = null);
}
