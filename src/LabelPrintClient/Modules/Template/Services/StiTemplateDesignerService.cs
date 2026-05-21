using System.Data;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.Template.Services;
using Stimulsoft.Report;
using Stimulsoft.Report.Components;

namespace LabelPrintClient.Modules.Template.Services;

public class StiTemplateDesignerService
{
    private readonly ILabelTemplateStorageService _storage;

    public StiTemplateDesignerService(ILabelTemplateStorageService storage)
    {
        _storage = storage;
    }

    public async Task DesignAsync(
        LabelTemplate template,
        IReadOnlyList<LabelTemplateField> fields,
        CancellationToken cancellationToken = default)
    {
        var report = await StaThreadRunner.RunAsync(() =>
        {
            var designReport = LoadOrCreateReport(template);
            RegisterDesignData(designReport, template.DataSourceName, fields);
            designReport.Design(true);
            return designReport;
        }, cancellationToken).ConfigureAwait(false);

        await _storage.SaveReportAsync(template, report, cancellationToken).ConfigureAwait(false);
    }

    private StiReport LoadOrCreateReport(LabelTemplate template)
    {
        try
        {
            return _storage.LoadReport(template);
        }
        catch
        {
            var report = new StiReport();
            report.Pages.Clear();
            var page = new StiPage();
            page.Name = "Page1";
            report.Pages.Add(page);
            return report;
        }
    }

    private static void RegisterDesignData(StiReport report, string dataSourceName, IReadOnlyList<LabelTemplateField> fields)
    {
        var table = new DataTable(dataSourceName);
        foreach (var field in fields)
        {
            if (!table.Columns.Contains(field.FieldCode))
                table.Columns.Add(field.FieldCode, typeof(string));
        }

        var row = table.NewRow();
        foreach (var field in fields)
        {
            row[field.FieldCode] = $"示例{field.FieldName}";
        }
        table.Rows.Add(row);

        report.Dictionary.Databases.Clear();
        report.RegData(dataSourceName, table);
        report.Dictionary.Synchronize();
    }
}
