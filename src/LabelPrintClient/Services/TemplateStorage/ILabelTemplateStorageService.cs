using LabelPrintClient.Models;
using Stimulsoft.Report;

namespace LabelPrintClient.Services.TemplateStorage;

public interface ILabelTemplateStorageService
{
    StiReport LoadReport(LabelTemplate template);

    void SaveReport(LabelTemplate template, StiReport report);
}
