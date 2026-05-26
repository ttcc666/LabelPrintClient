using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.PrintCenter;

public class SerializedPrintFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task PrintSelectedRows_ForSerializedTemplate_GeneratesSequentialSerialNumbers()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Serialized,
            serialPattern: "SN-{ProductCode}-{seq:0000}");
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.SerialNo, "序列号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var printer = new FakePrintExecutor();
        var service = new LabelPrintService(database.Settings, printer);

        await service.PrintSelectedRowsAsync(template.Id, batch.Id, [importRow.Id], printerName: null, copyCount: 2);

        var printedRows = await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.ImportRowId == importRow.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();
        var row = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);
        var counter = await AppDb.Db.Queryable<LabelSerialCounter>()
            .Where(x => x.TemplateId == template.Id && x.ImportRowId == importRow.Id)
            .SingleAsync();
        var serials = printedRows
            .Select(x => JsonHelper.Deserialize<Dictionary<string, string>>(x.RowDataJson)![TemplateSystemFields.SerialNo])
            .ToList();

        Assert.Equal(["SN-P001-0001", "SN-P001-0002"], serials);
        Assert.True(row.IsPrinted);
        Assert.Equal(2, row.PrintCount);
        Assert.Equal(2, counter.CurrentValue);
        Assert.DoesNotContain("serial_no", printer.Calls.Single().SelectMany(x => x.Keys));
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task ReprintJob_UsesHistoricalSerialNumbersWithoutAdvancingCounter()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Serialized,
            serialPattern: "SN-{seq:0000}");
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.SerialNo, "序列号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var printer = new FakePrintExecutor();
        var service = new LabelPrintService(database.Settings, printer);
        await service.PrintSelectedRowsAsync(template.Id, batch.Id, [importRow.Id], printerName: null);
        var job = await AppDb.Db.Queryable<LabelPrintJob>().SingleAsync();
        var counterBefore = await AppDb.Db.Queryable<LabelSerialCounter>().SingleAsync();

        await service.ReprintJobAsync(job.Id, printerName: null);

        var counterAfter = await AppDb.Db.Queryable<LabelSerialCounter>().SingleAsync();
        var firstPrintSerial = printer.Calls[0].Single()[TemplateSystemFields.SerialNo];
        var reprintSerial = printer.Calls[1].Single()[TemplateSystemFields.SerialNo];

        Assert.Equal("SN-0001", firstPrintSerial);
        Assert.Equal(firstPrintSerial, reprintSerial);
        Assert.Equal(counterBefore.CurrentValue, counterAfter.CurrentValue);
    }
}
