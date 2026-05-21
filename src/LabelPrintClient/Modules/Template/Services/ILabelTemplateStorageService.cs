using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using Stimulsoft.Report;

namespace LabelPrintClient.Modules.Template.Services;

public interface ILabelTemplateStorageService
{
    StiReport LoadReport(LabelTemplate template);

    Task<StiReport> LoadReportAsync(LabelTemplate template, CancellationToken cancellationToken = default);

    Task SaveReportAsync(LabelTemplate template, StiReport report, CancellationToken cancellationToken = default);
}

