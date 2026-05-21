using System.Data;
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
    private const int MaxCopyCount = 999;

    private readonly AppSettings _settings;
    private readonly ILabelTemplateStorageService _templateStorage;

    public LabelPrintService(AppSettings settings)
    {
        _settings = settings;
        _templateStorage = LabelTemplateStorageFactory.Create(settings.RunMode);
    }

    public void PreviewSelectedRows(long templateId, long batchId, IReadOnlyCollection<long> selectedRowIds, int copyCount = 1)
    {
        copyCount = ValidateCopyCount(copyCount);
        var context = BuildPrintContext(templateId, batchId, selectedRowIds, copyCount);
        var report = BuildRenderedReport(context.Template, context.DataTable);
        report.Show();
    }

    public void PrintSelectedRows(long templateId, long batchId, IReadOnlyCollection<long> selectedRowIds, string? printerName, int copyCount = 1)
    {
        copyCount = ValidateCopyCount(copyCount);
        var context = BuildPrintContext(templateId, batchId, selectedRowIds, 1);

        var printJob = new LabelPrintJob
        {
            Id = IdHelper.NewId(),
            TemplateId = context.Template.Id,
            BatchId = batchId,
            TemplateName = context.Template.Name,
            SelectedRowCount = context.Rows.Count * copyCount,
            PrinterName = printerName,
            Status = "Printing",
            OperatorName = _settings.OperatorName,
            CreateTime = DateTime.Now
        };

        var jobRows = context.Rows
            .SelectMany(row => Enumerable.Range(0, copyCount).Select(_ => new LabelPrintJobRow
            {
                Id = IdHelper.NewId(),
                PrintJobId = printJob.Id,
                ImportRowId = row.Id,
                RowIndex = row.RowIndex,
                RowDataJson = row.RowDataJson
            }))
            .ToList();

        AppDb.Db.Ado.UseTran(() =>
        {
            AppDb.Db.Insertable(printJob).ExecuteCommand();
            AppDb.Db.Insertable(jobRows).ExecuteCommand();
        });

        try
        {
            PrintLabels(context, printerName, copyCount);
            MarkPrinted(printJob, context.Rows, copyCount);
        }
        catch (Exception ex)
        {
            printJob.Status = "Failed";
            printJob.ErrorMessage = ex.Message;
            AppDb.Db.Updateable(printJob).ExecuteCommand();
            throw;
        }
    }

    private PrintContext BuildPrintContext(long templateId, long batchId, IReadOnlyCollection<long> selectedRowIds, int copyCount)
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

        var dataTable = DataTableBuilder.Build(rows, fields, template.DataSourceName, copyCount);
        return new PrintContext(template, fields, rows, dataTable);
    }

    private void PrintLabels(PrintContext context, string? printerName, int copyCount)
    {
        var settings = CreatePrinterSettings(printerName);

        foreach (var row in context.Rows)
        {
            for (var copyIndex = 0; copyIndex < copyCount; copyIndex++)
            {
                var dataTable = DataTableBuilder.Build(
                    new[] { row },
                    context.Fields,
                    context.Template.DataSourceName);

                var report = BuildRenderedReport(context.Template, dataTable);
                report.Print(false, settings);
            }
        }
    }

    private StiReport BuildRenderedReport(LabelTemplate template, DataTable dataTable)
    {
        var report = _templateStorage.LoadReport(template);
        report.Dictionary.Databases.Clear();
        report.RegData(template.DataSourceName, dataTable);
        report.Dictionary.Synchronize();
        report.Render();
        return report;
    }

    private static PrinterSettings CreatePrinterSettings(string? printerName)
    {
        var settings = new PrinterSettings
        {
            Copies = 1
        };

        if (!string.IsNullOrWhiteSpace(printerName))
            settings.PrinterName = printerName;

        if (!settings.IsValid)
            throw new InvalidOperationException($"打印机“{settings.PrinterName}”不可用，请检查打印机名称或重新选择打印机。");

        return settings;
    }

    private static int ValidateCopyCount(int copyCount)
    {
        if (copyCount < 1 || copyCount > MaxCopyCount)
            throw new InvalidOperationException($"打印份数必须是 1 到 {MaxCopyCount} 之间的整数。");

        return copyCount;
    }

    private static void MarkPrinted(LabelPrintJob printJob, List<LabelImportRow> rows, int copyCount)
    {
        foreach (var row in rows)
        {
            row.IsPrinted = true;
            row.PrintCount += copyCount;
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
