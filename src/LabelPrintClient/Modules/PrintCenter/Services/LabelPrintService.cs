using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.Template.Services;
using Stimulsoft.Report;
using Stimulsoft.Report.Components;
using System.Data;
using System.Drawing.Printing;

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
        var previewRows = await BuildPreviewJobRowsAsync(context, copyCount, cancellationToken)
            .ConfigureAwait(false);

        await StaThreadRunner.RunAsync(() =>
        {
            StiReport? mainReport = null;

            foreach (var jobRow in previewRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dataTable = BuildDataTableFromJobRow(context.Template.DataSourceName, context.Fields, jobRow);
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
        List<LabelPrintJobRow> jobRows = new();

        await ExecuteTransactionAsync(async () =>
        {
            jobRows = await BuildPrintJobRowsAsync(printJob.Id, context.Template, context.Batch, context.Rows, copyCount, cancellationToken)
                .ConfigureAwait(false);

            await AppDb.Db.Insertable(printJob).ExecuteCommandAsync().ConfigureAwait(false);
            await AppDb.Db.Insertable(jobRows).ExecuteCommandAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        try
        {
            await Task.Run(() => PrintJobRows(context.Template, context.Fields, jobRows, printerName, cancellationToken, progress, "开始提交打印任务"), cancellationToken)
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

        if (rows.Count == 0)
            throw new InvalidOperationException("当前打印任务没有可重打印的明细。");

        var expandedRows = rows
            .SelectMany(row => Enumerable.Range(0, ValidateCopyCount(copyCount)).Select(_ => row))
            .ToList();
        await PrintHistoryRowsAsync(job.TemplateId, expandedRows, printerName, cancellationToken, progress)
            .ConfigureAwait(false);
    }

    public async Task RetryFailedJobAsync(
        long printJobId,
        string? printerName,
        CancellationToken cancellationToken = default,
        IProgress<BackgroundTaskProgress>? progress = null)
    {
        var job = await LoadPrintJobAsync(printJobId).ConfigureAwait(false);
        if (!string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只有失败的打印任务可以失败重试。");

        var jobRows = await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.PrintJobId == printJobId)
            .OrderBy(x => x.RowIndex)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (jobRows.Count == 0)
            throw new InvalidOperationException("当前失败任务没有可重试的打印明细。");

        try
        {
            await PrintHistoryRowsAsync(job.TemplateId, jobRows, printerName, cancellationToken, progress)
                .ConfigureAwait(false);
            await MarkRetryPrintedAsync(job, jobRows).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            await AppDb.Db.Updateable(job).ExecuteCommandAsync().ConfigureAwait(false);
            throw;
        }
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
        var rows = Enumerable.Range(0, ValidateCopyCount(copyCount)).Select(_ => jobRow).ToList();
        await PrintHistoryRowsAsync(job.TemplateId, rows, printerName, cancellationToken, progress).ConfigureAwait(false);
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

        var batches = await AppDb.Db.Queryable<LabelImportBatch>()
            .Where(x => x.Id == batchId)
            .Take(1)
            .ToListAsync()
            .ConfigureAwait(false);
        var batch = batches.FirstOrDefault()
            ?? throw new InvalidOperationException("导入批次不存在。");

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId && !x.IsDeleted)
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
        return new PrintContext(template, batch, fields, rows, dataTable);
    }

    private void PrintJobRows(
        LabelTemplate template,
        IReadOnlyList<LabelTemplateField> fields,
        IReadOnlyList<LabelPrintJobRow> jobRows,
        string? printerName,
        CancellationToken cancellationToken,
        IProgress<BackgroundTaskProgress>? progress,
        string startMessage)
    {
        var settings = CreatePrinterSettings(printerName);
        var total = jobRows.Count;
        var completed = 0;
        var progressReportInterval = Math.Max(1, (int)Math.Ceiling(total / 100.0));
        progress?.Report(new BackgroundTaskProgress(completed, total, startMessage));

        foreach (var jobRow in jobRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dataTable = BuildDataTableFromJobRow(template.DataSourceName, fields, jobRow);
            var report = BuildRenderedReport(template, dataTable);
            report.Print(false, settings);
            completed++;
            if (ShouldReportProgress(completed, total, progressReportInterval))
                progress?.Report(new BackgroundTaskProgress(completed, total, "已提交打印明细"));
        }
    }

    private static bool ShouldReportProgress(int completed, int total, int reportInterval)
    {
        return completed >= total || completed % reportInterval == 0;
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

    private static async Task<List<LabelPrintJobRow>> BuildPrintJobRowsAsync(
        long printJobId,
        LabelTemplate template,
        LabelImportBatch batch,
        IEnumerable<LabelImportRow> rows,
        int copyCount,
        CancellationToken cancellationToken)
    {
        var jobRows = new List<LabelPrintJobRow>();
        var now = DateTime.Now;
        foreach (var row in rows)
        {
            for (var copyIndex = 0; copyIndex < copyCount; copyIndex++)
            {
                var rowDataJson = row.RowDataJson;
                if (template.IsSerialNumber == true)
                {
                    var serialNo = await SerialNumberService.GenerateNextAsync(template, row, now, cancellationToken)
                        .ConfigureAwait(false);
                    var dict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>();
                    dict["serial_no"] = serialNo;
                    rowDataJson = JsonHelper.Serialize(dict);
                }
                else
                {
                    var dict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>();
                    if (!dict.TryGetValue("batch_no", out var bNo) || string.IsNullOrWhiteSpace(bNo))
                    {
                        if (!string.IsNullOrWhiteSpace(batch.BatchNo))
                        {
                            dict["batch_no"] = batch.BatchNo;
                            rowDataJson = JsonHelper.Serialize(dict);
                        }
                    }
                }

                jobRows.Add(new LabelPrintJobRow
                {
                    Id = IdHelper.NewId(),
                    PrintJobId = printJobId,
                    ImportRowId = row.Id,
                    RowIndex = row.RowIndex,
                    RowDataJson = rowDataJson
                });
            }
        }
        return jobRows;
    }

    private static async Task<List<LabelPrintJobRow>> BuildPreviewJobRowsAsync(
        PrintContext context,
        int copyCount,
        CancellationToken cancellationToken)
    {
        var jobRows = new List<LabelPrintJobRow>();
        var now = DateTime.Now;
        foreach (var row in context.Rows)
        {
            var currentSerial = context.Template.IsSerialNumber == true
                ? await SerialNumberService.GetCurrentValueAsync(context.Template, row, now, cancellationToken).ConfigureAwait(false)
                : 0;
            for (var copyIndex = 0; copyIndex < copyCount; copyIndex++)
            {
                var rowDataJson = row.RowDataJson;
                if (context.Template.IsSerialNumber == true)
                {
                    var pattern = SerialNumberService.NormalizePattern(context.Template.SerialNumberPattern, context.Template.SerialNumberPrefix);
                    var dict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>();
                    dict["serial_no"] = SerialNumberService.Preview(pattern, now, ++currentSerial, dict);
                    rowDataJson = JsonHelper.Serialize(dict);
                }
                else
                {
                    var dict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>();
                    if (!dict.TryGetValue("batch_no", out var bNo) || string.IsNullOrWhiteSpace(bNo))
                    {
                        if (!string.IsNullOrWhiteSpace(context.Batch.BatchNo))
                        {
                            dict["batch_no"] = context.Batch.BatchNo;
                            rowDataJson = JsonHelper.Serialize(dict);
                        }
                    }
                }

                jobRows.Add(new LabelPrintJobRow
                {
                    Id = IdHelper.NewId(),
                    PrintJobId = 0,
                    ImportRowId = row.Id,
                    RowIndex = row.RowIndex,
                    RowDataJson = rowDataJson
                });
            }
        }
        return jobRows;
    }

    private static DataTable BuildDataTableFromJobRow(
        string dataSourceName,
        IReadOnlyList<LabelTemplateField> fields,
        LabelPrintJobRow jobRow)
    {
        var dict = JsonHelper.Deserialize<Dictionary<string, string>>(jobRow.RowDataJson) ?? new Dictionary<string, string>();
        var table = new DataTable(dataSourceName);
        foreach (var field in fields)
        {
            if (!table.Columns.Contains(field.FieldCode))
                table.Columns.Add(field.FieldCode, typeof(string));
        }

        var dataRow = table.NewRow();
        foreach (var field in fields)
        {
            dataRow[field.FieldCode] = dict.TryGetValue(field.FieldCode, out var value) ? value : string.Empty;
        }
        table.Rows.Add(dataRow);
        return table;
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

    private static async Task MarkRetryPrintedAsync(LabelPrintJob printJob, IReadOnlyList<LabelPrintJobRow> jobRows)
    {
        var rowCounts = jobRows
            .GroupBy(x => x.ImportRowId)
            .ToDictionary(x => x.Key, x => x.Count());
        var rowIds = rowCounts.Keys.ToList();
        var rows = await AppDb.Db.Queryable<LabelImportRow>()
            .Where(x => rowIds.Contains(x.Id))
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            row.IsPrinted = true;
            row.PrintCount += rowCounts.TryGetValue(row.Id, out var count) ? count : 0;
            row.LastPrintTime = DateTime.Now;
        }

        printJob.Status = "Printed";
        printJob.PrintTime = DateTime.Now;
        printJob.ErrorMessage = null;

        await ExecuteTransactionAsync(async () =>
        {
            await AppDb.Db.Updateable(printJob).ExecuteCommandAsync().ConfigureAwait(false);
            if (rows.Count > 0)
                await AppDb.Db.Updateable(rows).ExecuteCommandAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task PrintHistoryRowsAsync(
        long templateId,
        List<LabelPrintJobRow> jobRows,
        string? printerName,
        CancellationToken cancellationToken = default,
        IProgress<BackgroundTaskProgress>? progress = null)
    {
        var templates = await AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.Id == templateId)
            .Take(1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var template = templates.FirstOrDefault()
            ?? throw new InvalidOperationException("模板不存在。");

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var settings = CreatePrinterSettings(printerName);
        var total = jobRows.Count;
        var completed = 0;
        progress?.Report(new BackgroundTaskProgress(completed, total, "开始提交历史重打任务"));

        await Task.Run(() =>
        {
            foreach (var jobRow in jobRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var table = BuildDataTableFromJobRow(template.DataSourceName, fields, jobRow);
                var report = BuildRenderedReport(template, table);
                report.Print(false, settings);

                completed++;
                progress?.Report(new BackgroundTaskProgress(completed, total, "已提交历史重打明细"));
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteTransactionAsync(Func<Task> operation)
    {
        await AppDb.UseTranAsync(operation).ConfigureAwait(false);
    }
}
