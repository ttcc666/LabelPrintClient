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

    public async Task PreviewSelectedRowsAsync(
        long templateId,
        long batchId,
        IReadOnlyCollection<long> selectedRowIds,
        int copyCount = 1,
        CancellationToken cancellationToken = default)
    {
        copyCount = ValidateCopyCount(copyCount);
        var context = await BuildPrintContextAsync(templateId, batchId, selectedRowIds, copyCount, cancellationToken)
            .ConfigureAwait(false);
        await StaThreadRunner.RunAsync(() =>
        {
            var report = BuildRenderedReport(context.Template, context.DataTable);
            report.Show(true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PrintSelectedRowsAsync(
        long templateId,
        long batchId,
        IReadOnlyCollection<long> selectedRowIds,
        string? printerName,
        int copyCount = 1,
        CancellationToken cancellationToken = default)
    {
        copyCount = ValidateCopyCount(copyCount);
        var context = await BuildPrintContextAsync(templateId, batchId, selectedRowIds, 1, cancellationToken)
            .ConfigureAwait(false);

        var printJob = BuildPrintJob(context.Template, batchId, context.Rows.Count * copyCount, printerName, _settings.OperatorName);
        var jobRows = BuildPrintJobRows(printJob.Id, context.Rows, copyCount);

        await ExecuteTransactionAsync(async () =>
        {
            await AppDb.Db.Insertable(printJob).ExecuteCommandAsync().ConfigureAwait(false);
            await AppDb.Db.Insertable(jobRows).ExecuteCommandAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        try
        {
            await Task.Run(() => PrintLabels(context, printerName, copyCount, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            await MarkPrintedAsync(printJob, context.Rows, copyCount).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            printJob.Status = "Failed";
            printJob.ErrorMessage = ex.Message;
            await AppDb.Db.Updateable(printJob).ExecuteCommandAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<PrintContext> BuildPrintContextAsync(
        long templateId,
        long batchId,
        IReadOnlyCollection<long> selectedRowIds,
        int copyCount,
        CancellationToken cancellationToken)
    {
        if (selectedRowIds.Count == 0)
            throw new InvalidOperationException("请选择要打印的数据行。");

        var templates = await AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.Id == templateId)
            .Take(1)
            .ToListAsync()
            .ConfigureAwait(false);
        var template = templates.FirstOrDefault()
            ?? throw new InvalidOperationException("模板不存在。");

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId)
            .OrderBy(x => x.Sort)
            .ToListAsync()
            .ConfigureAwait(false);

        var ids = selectedRowIds.ToList();
        var rows = await AppDb.Db.Queryable<LabelImportRow>()
            .Where(x => x.BatchId == batchId && ids.Contains(x.Id) && x.IsValid)
            .OrderBy(x => x.RowIndex)
            .ToListAsync()
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (rows.Count == 0)
            throw new InvalidOperationException("选中的数据中没有有效行，无法打印。");

        var dataTable = DataTableBuilder.Build(rows, fields, template.DataSourceName, copyCount);
        return new PrintContext(template, fields, rows, dataTable);
    }

    private void PrintLabels(PrintContext context, string? printerName, int copyCount, CancellationToken cancellationToken)
    {
        var settings = CreatePrinterSettings(printerName);

        foreach (var row in context.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var copyIndex = 0; copyIndex < copyCount; copyIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
        RegisterReportData(report, template.DataSourceName, dataTable);
        report.Render(false);
        return report;
    }

    private static void RegisterReportData(StiReport report, string dataSourceName, DataTable dataTable)
    {
        report.Dictionary.Databases.Clear();
        report.RegData(dataSourceName, dataTable);
        report.Dictionary.Synchronize();
    }

    private static LabelPrintJob BuildPrintJob(
        LabelTemplate template,
        long batchId,
        int selectedRowCount,
        string? printerName,
        string? operatorName)
    {
        return new LabelPrintJob
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            BatchId = batchId,
            TemplateName = template.Name,
            SelectedRowCount = selectedRowCount,
            PrinterName = printerName,
            Status = "Printing",
            OperatorName = operatorName,
            CreateTime = DateTime.Now
        };
    }

    private static List<LabelPrintJobRow> BuildPrintJobRows(long printJobId, IEnumerable<LabelImportRow> rows, int copyCount)
    {
        return rows
            .SelectMany(row => Enumerable.Range(0, copyCount).Select(_ => new LabelPrintJobRow
            {
                Id = IdHelper.NewId(),
                PrintJobId = printJobId,
                ImportRowId = row.Id,
                RowIndex = row.RowIndex,
                RowDataJson = row.RowDataJson
            }))
            .ToList();
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

    private static async Task MarkPrintedAsync(LabelPrintJob printJob, List<LabelImportRow> rows, int copyCount)
    {
        foreach (var row in rows)
        {
            row.IsPrinted = true;
            row.PrintCount += copyCount;
            row.LastPrintTime = DateTime.Now;
        }

        printJob.Status = "Printed";
        printJob.PrintTime = DateTime.Now;

        await ExecuteTransactionAsync(async () =>
        {
            await AppDb.Db.Updateable(printJob).ExecuteCommandAsync().ConfigureAwait(false);
            await AppDb.Db.Updateable(rows).ExecuteCommandAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static async Task ExecuteTransactionAsync(Func<Task> operation)
    {
        await AppDb.Db.Ado.BeginTranAsync().ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
            await AppDb.Db.Ado.CommitTranAsync().ConfigureAwait(false);
        }
        catch
        {
            await AppDb.Db.Ado.RollbackTranAsync().ConfigureAwait(false);
            throw;
        }
    }
}
