using System.Data;
using LabelPrintClient.Models;
using LabelPrintClient.Services.TemplateStorage;
using Stimulsoft.Report;
using Stimulsoft.Report.Components;

namespace LabelPrintClient.Services.Stimulsoft;

public class StiTemplateDesignerService
{
    private readonly ILabelTemplateStorageService _storage;

    public StiTemplateDesignerService(ILabelTemplateStorageService storage)
    {
        _storage = storage;
    }

    public void Design(LabelTemplate template, IReadOnlyList<LabelTemplateField> fields)
    {
        StiReport report;
        try
        {
            report = _storage.LoadReport(template);
        }
        catch
        {
            report = new StiReport();
            report.Pages.Clear();
            var page = new StiPage();
            page.Name = "Page1";
            report.Pages.Add(page);
        }

        RegisterDesignData(report, template.DataSourceName, fields);
        report.DesignV2WithWpf();
        _storage.SaveReport(template, report);
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
