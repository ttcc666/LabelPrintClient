using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.PrintCenter;

public class BatchPrintFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task PrintSelectedRows_LocksBatchNumberAfterSuccessfulPrint()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Batch,
            batchPattern: "BATCH-{ProductCode}-{yyyy}{MM}{dd}");
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var printer = new FakePrintExecutor();
        var service = new LabelPrintService(database.Settings, printer);

        await service.PrintSelectedRowsAsync(template.Id, batch.Id, [importRow.Id], printerName: null);

        var savedRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);
        var savedData = JsonHelper.Deserialize<Dictionary<string, string>>(savedRow.RowDataJson)!;
        var jobRows = await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.ImportRowId == importRow.Id)
            .ToListAsync();
        var jobRowData = JsonHelper.Deserialize<Dictionary<string, string>>(jobRows.Single().RowDataJson)!;

        Assert.True(savedRow.IsPrinted);
        Assert.Equal(1, savedRow.PrintCount);
        Assert.StartsWith("BATCH-P001-", savedData[TemplateSystemFields.BatchNo]);
        Assert.Equal(savedData[TemplateSystemFields.BatchNo], jobRowData[TemplateSystemFields.BatchNo]);
        Assert.DoesNotContain("batch_no", savedData.Keys);
        Assert.Single(printer.Calls);
        Assert.Equal(savedData[TemplateSystemFields.BatchNo], printer.Calls.Single().Single()[TemplateSystemFields.BatchNo]);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task RetryFailedJob_WhenOriginalImportRowIsMissing_KeepsFailedJobRows()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var job = await TestSeed.FailedPrintJobAsync(template, batch);
        var originalJobRow = await TestSeed.PrintJobRowAsync(job, importRow, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        await AppDb.Db.Deleteable<LabelImportRow>().Where(x => x.Id == importRow.Id).ExecuteCommandAsync();
        var service = new LabelPrintService(database.Settings, new FakePrintExecutor());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RetryFailedJobAsync(job.Id, printerName: null));

        var remainingRows = await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.PrintJobId == job.Id)
            .ToListAsync();
        var savedJob = await AppDb.Db.Queryable<LabelPrintJob>().InSingleAsync(job.Id);

        Assert.Contains("原始导入行", ex.Message);
        Assert.Single(remainingRows);
        Assert.Equal(originalJobRow.Id, remainingRows.Single().Id);
        Assert.Equal("Failed", savedJob.Status);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task PrintSelectedRows_WhenBatchNumberAlreadyLocked_ReusesExistingValue()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Batch,
            batchPattern: "BATCH-{ProductCode}-{yyyy}{MM}{dd}");
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-LOCKED"
        });
        var printer = new FakePrintExecutor();
        var service = new LabelPrintService(database.Settings, printer);

        await service.PrintSelectedRowsAsync(template.Id, batch.Id, [importRow.Id], printerName: null);

        var savedRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);
        var savedData = JsonHelper.Deserialize<Dictionary<string, string>>(savedRow.RowDataJson)!;
        var printedData = printer.Calls.Single().Single();

        Assert.Equal("BATCH-LOCKED", savedData[TemplateSystemFields.BatchNo]);
        Assert.Equal("BATCH-LOCKED", printedData[TemplateSystemFields.BatchNo]);
        Assert.Equal(1, savedRow.PrintCount);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task PrintSelectedRows_WhenFirstBatchPrintFails_DoesNotLockBatchNumber()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var service = new LabelPrintService(database.Settings, new FakePrintExecutor { ThrowOnPrint = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrintSelectedRowsAsync(template.Id, batch.Id, [importRow.Id], printerName: null));

        var savedRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);
        var savedData = JsonHelper.Deserialize<Dictionary<string, string>>(savedRow.RowDataJson)!;

        Assert.False(savedRow.IsPrinted);
        Assert.Equal(0, savedRow.PrintCount);
        Assert.DoesNotContain(TemplateSystemFields.BatchNo, savedData.Keys);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task RetryFailedJob_ForBatchTemplate_RegeneratesRowsAndLocksBatchNumber()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Batch,
            batchPattern: "BATCH-{ProductCode}");
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var job = await TestSeed.FailedPrintJobAsync(template, batch);
        var oldJobRow = await TestSeed.PrintJobRowAsync(job, importRow, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var printer = new FakePrintExecutor();
        var service = new LabelPrintService(database.Settings, printer);

        await service.RetryFailedJobAsync(job.Id, printerName: null);

        var savedJob = await AppDb.Db.Queryable<LabelPrintJob>().InSingleAsync(job.Id);
        var savedImportRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);
        var savedData = JsonHelper.Deserialize<Dictionary<string, string>>(savedImportRow.RowDataJson)!;
        var jobRows = await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.PrintJobId == job.Id)
            .ToListAsync();

        Assert.Equal("Printed", savedJob.Status);
        Assert.Null(savedJob.ErrorMessage);
        Assert.True(savedImportRow.IsPrinted);
        Assert.Equal(1, savedImportRow.PrintCount);
        Assert.Equal("BATCH-P001", savedData[TemplateSystemFields.BatchNo]);
        Assert.Single(jobRows);
        Assert.NotEqual(oldJobRow.Id, jobRows.Single().Id);
        Assert.Equal("BATCH-P001", printer.Calls.Single().Single()[TemplateSystemFields.BatchNo]);
    }
}
