using LabelPrintClient.Models;
using Stimulsoft.Report;

namespace LabelPrintClient.Services.TemplateStorage;

public interface ILabelTemplateStorageService
{
    StiReport LoadReport(LabelTemplate template);

    Task<StiReport> LoadReportAsync(LabelTemplate template, CancellationToken cancellationToken = default);

    Task SaveReportAsync(LabelTemplate template, StiReport report, CancellationToken cancellationToken = default);
}
