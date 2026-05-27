using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.Template.Services;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.Template;

public class TemplateBatchBindingFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task ClearLockedBatchNumbersAsync_RemovesBatchNumberFromPrintedImportRowsOnly()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        var batch = await TestSeed.ImportBatchAsync(template, totalRows: 2);
        var printedRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-OLD"
        });
        printedRow.IsPrinted = true;
        printedRow.PrintCount = 1;
        await AppDb.Db.Updateable(printedRow).ExecuteCommandAsync();
        var unprintedRow = await TestSeed.ImportRowAsync(batch, 2, new Dictionary<string, string>
        {
            ["ProductCode"] = "P002",
            [TemplateSystemFields.BatchNo] = "BATCH-KEEP"
        });

        var changedCount = await TemplateBatchBindingService.ClearLockedBatchNumbersAsync(template.Id);

        var savedPrintedRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(printedRow.Id);
        var savedUnprintedRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(unprintedRow.Id);
        var printedData = JsonHelper.Deserialize<Dictionary<string, string>>(savedPrintedRow.RowDataJson)!;
        var unprintedData = JsonHelper.Deserialize<Dictionary<string, string>>(savedUnprintedRow.RowDataJson)!;

        Assert.Equal(1, changedCount);
        Assert.DoesNotContain(TemplateSystemFields.BatchNo, printedData.Keys);
        Assert.Equal("P001", printedData["ProductCode"]);
        Assert.DoesNotContain("BATCH-OLD", savedPrintedRow.SearchText);
        Assert.Equal("BATCH-KEEP", unprintedData[TemplateSystemFields.BatchNo]);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task ClearLockedBatchNumbersAsync_DoesNotChangeHistoricalPrintJobRows()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-OLD"
        });
        importRow.IsPrinted = true;
        await AppDb.Db.Updateable(importRow).ExecuteCommandAsync();
        var job = await TestSeed.FailedPrintJobAsync(template, batch);
        job.Status = "Printed";
        await AppDb.Db.Updateable(job).ExecuteCommandAsync();
        var jobRow = await TestSeed.PrintJobRowAsync(job, importRow, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-HISTORY"
        });

        await TemplateBatchBindingService.ClearLockedBatchNumbersAsync(template.Id);

        var savedJobRow = await AppDb.Db.Queryable<LabelPrintJobRow>().InSingleAsync(jobRow.Id);
        var historyData = JsonHelper.Deserialize<Dictionary<string, string>>(savedJobRow.RowDataJson)!;

        Assert.Equal("BATCH-HISTORY", historyData[TemplateSystemFields.BatchNo]);
    }
}
