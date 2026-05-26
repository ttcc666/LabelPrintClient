using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.PrintCenter;

public class ReprintFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task ReprintJobRow_UsesHistoricalBatchNumberAndDoesNotMutateImportRow()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-LOCKED"
        });
        var job = await TestSeed.FailedPrintJobAsync(template, batch);
        job.Status = "Printed";
        await AppDb.Db.Updateable(job).ExecuteCommandAsync();
        var jobRow = await TestSeed.PrintJobRowAsync(job, importRow, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-HISTORY"
        });
        var printer = new FakePrintExecutor();
        var service = new LabelPrintService(database.Settings, printer);

        await service.ReprintJobRowAsync(jobRow.Id, printerName: null, copyCount: 2);

        var savedImportRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);
        var savedImportData = JsonHelper.Deserialize<Dictionary<string, string>>(savedImportRow.RowDataJson)!;
        var printed = printer.Calls.Single();

        Assert.Equal(2, printed.Count);
        Assert.All(printed, row => Assert.Equal("BATCH-HISTORY", row[TemplateSystemFields.BatchNo]));
        Assert.Equal("BATCH-LOCKED", savedImportData[TemplateSystemFields.BatchNo]);
        Assert.Equal(0, savedImportRow.PrintCount);
    }
}
