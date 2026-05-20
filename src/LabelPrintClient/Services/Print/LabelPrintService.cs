using System.Drawing.Printing;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using LabelPrintClient.Services.TemplateStorage;
using Stimulsoft.Report;

namespace LabelPrintClient.Services.Print;

public class LabelPrintService
{
    private readonly AppSettings _settings;
    private readonly ILabelTemplateStorageService _templateStorage;

    public LabelPrintService(AppSettings settings)
    {
        _settings = settings;
        _templateStorage = LabelTemplateStorageFactory.Create(settings.RunMode);
    }

    public void PreviewSelectedRows(long templateId, long batchId, IReadOnlyCollection<long> selectedRowIds)
    {
        var context = BuildPrintContext(templateId, batchId, selectedRowIds);
        var report = _templateStorage.LoadReport(context.Template);
        report.Dictionary.Databases.Clear();
        report.RegData(context.Template.DataSourceName, context.DataTable);
        report.Dictionary.Synchronize();
        report.Render();
        report.ShowWithWpf();
    }

    public void PrintSelectedRows(long templateId, long batchId, IReadOnlyCollection<long> selectedRowIds, string? printerName)
    {
        var context = BuildPrintContext(templateId, batchId, selectedRowIds);

        var printJob = new LabelPrintJob
        {
            Id = IdHelper.NewId(),
            TemplateId = context.Template.Id,
            BatchId = batchId,
            TemplateName = context.Template.Name,
            SelectedRowCount = context.Rows.Count,
            PrinterName = printerName,
            Status = "Printing",
            OperatorName = _settings.OperatorName,
            CreateTime = DateTime.Now
        };

        var jobRows = context.Rows.Select(x => new LabelPrintJobRow
        {
            Id = IdHelper.NewId(),
            PrintJobId = printJob.Id,
            ImportRowId = x.Id,
            RowIndex = x.RowIndex,
            RowDataJson = x.RowDataJson
        }).ToList();

        AppDb.Db.Ado.UseTran(() =>
        {
            AppDb.Db.Insertable(printJob).ExecuteCommand();
            AppDb.Db.Insertable(jobRows).ExecuteCommand();
        });

        try
        {
            var report = _templateStorage.LoadReport(context.Template);
            report.Dictionary.Databases.Clear();
            report.RegData(context.Template.DataSourceName, context.DataTable);
            report.Dictionary.Synchronize();
            report.Render();

            if (!string.IsNullOrWhiteSpace(printerName))
            {
                var settings = new PrinterSettings { PrinterName = printerName };
                report.Print(false, settings);
            }
            else
            {
                report.PrintWithWpf();
            }

            MarkPrinted(printJob, context.Rows);
        }
        catch (Exception ex)
        {
            printJob.Status = "Failed";
            printJob.ErrorMessage = ex.Message;
            AppDb.Db.Updateable(printJob).ExecuteCommand();
            throw;
        }
    }

    private PrintContext BuildPrintContext(long templateId, long batchId, IReadOnlyCollection<long> selectedRowIds)
    {
        if (selectedRowIds.Count == 0)
            throw new InvalidOperationException("请选择要打印的数据行。");

        var template = AppDb.Db.Queryable<LabelTemplate>().First(x => x.Id == templateId)
            ?? throw new InvalidOperationException("模板不存在。");

        var fields = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId)
            .OrderBy(x => x.Sort)
            .ToList();

        var ids = selectedRowIds.ToList();

        var rows = AppDb.Db.Queryable<LabelImportRow>()
            .Where(x => x.BatchId == batchId && ids.Contains(x.Id) && x.IsValid)
            .OrderBy(x => x.RowIndex)
            .ToList();

        if (rows.Count == 0)
            throw new InvalidOperationException("选中的数据中没有有效行，无法打印。");

        var dataTable = DataTableBuilder.Build(rows, fields, template.DataSourceName);
        return new PrintContext(template, fields, rows, dataTable);
    }

    private static void MarkPrinted(LabelPrintJob printJob, List<LabelImportRow> rows)
    {
        foreach (var row in rows)
        {
            row.IsPrinted = true;
            row.PrintCount += 1;
            row.LastPrintTime = DateTime.Now;
        }

        printJob.Status = "Printed";
        printJob.PrintTime = DateTime.Now;

        AppDb.Db.Ado.UseTran(() =>
        {
            AppDb.Db.Updateable(printJob).ExecuteCommand();
            AppDb.Db.Updateable(rows).ExecuteCommand();
        });
    }
}
