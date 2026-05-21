using System.Data;
using System.Drawing.Printing;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.Template.Services;
using Stimulsoft.Report;
using Stimulsoft.Report.Components;

namespace LabelPrintClient.Modules.PrintCenter.Services;

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
        var context = await BuildPrintContextAsync(templateId, batchId, selectedRowIds, 1, cancellationToken)
            .ConfigureAwait(false);
        await StaThreadRunner.RunAsync(() =>
        {
            StiReport? mainReport = null;

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

                    var tempReport = BuildRenderedReport(context.Template, dataTable);

                    if (mainReport == null)
                    {
                        mainReport = tempReport;
                    }
                    else
                    {
                        foreach (StiPage page in tempReport.RenderedPages)
                        {
                            page.Report = mainReport;
                            mainReport.RenderedPages.Add(page);
                        }
                    }
                }
            }

            if (mainReport != null)
            {
                mainReport.Show(true);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PrintSelectedRowsAsync(
        long templateId,
        long batchId,
        IReadOnlyCollection<long> selectedRowIds,
        string? printerName,
        int copyCount = 1,
        CancellationToken cancellationToken = default,
        IProgress<BackgroundTaskProgress>? progress = null)
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
            await Task.Run(() => PrintLabels(context, printerName, copyCount, cancellationToken, progress), cancellationToken)
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

    public async Task ReprintJobAsync(
        long printJobId,
        string? printerName,
        int copyCount = 1,
        CancellationToken cancellationToken = default,
        IProgress<BackgroundTaskProgress>? progress = null)
    {
        var job = await LoadPrintJobAsync(printJobId).ConfigureAwait(false);
        var rows = await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.PrintJobId == printJobId)
            .OrderBy(x => x.RowIndex)
            .ToListAsync()
            .ConfigureAwait(false);

        var rowIds = rows
            .Select(x => x.ImportRowId)
            .Distinct()
            .ToList();

        if (rowIds.Count == 0)
            throw new InvalidOperationException("当前打印任务没有可重打印的明细。");

        await PrintSelectedRowsAsync(
            job.TemplateId,
            job.BatchId,
            rowIds,
            printerName,
            copyCount,
            cancellationToken,
            progress).ConfigureAwait(false);
    }

    public async Task ReprintJobRowAsync(
        long printJobRowId,
        string? printerName,
        int copyCount = 1,
        CancellationToken cancellationToken = default,
        IProgress<BackgroundTaskProgress>? progress = null)
    {
        var jobRows = await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.Id == printJobRowId)
            .Take(1)
            .ToListAsync()
            .ConfigureAwait(false);
        var jobRow = jobRows.FirstOrDefault()
            ?? throw new InvalidOperationException("打印明细不存在。");

        var job = await LoadPrintJobAsync(jobRow.PrintJobId).ConfigureAwait(false);
        await PrintSelectedRowsAsync(
            job.TemplateId,
            job.BatchId,
            new[] { jobRow.ImportRowId },
            printerName,
            copyCount,
            cancellationToken,
            progress).ConfigureAwait(false);
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

    private void PrintLabels(
        PrintContext context,
        string? printerName,
        int copyCount,
        CancellationToken cancellationToken,
        IProgress<BackgroundTaskProgress>? progress)
    {
        var settings = CreatePrinterSettings(printerName);
        var total = context.Rows.Count * copyCount;
        var completed = 0;
        progress?.Report(new BackgroundTaskProgress(completed, total, "开始提交打印任务"));

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
                completed++;
                progress?.Report(new BackgroundTaskProgress(completed, total, $"已提交 Excel 第 {row.RowIndex} 行"));
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

    private static async Task<LabelPrintJob> LoadPrintJobAsync(long printJobId)
    {
        var jobs = await AppDb.Db.Queryable<LabelPrintJob>()
            .Where(x => x.Id == printJobId)
            .Take(1)
            .ToListAsync()
            .ConfigureAwait(false);

        return jobs.FirstOrDefault()
            ?? throw new InvalidOperationException("打印任务不存在。");
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
