using LabelPrintClient.Database;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.PrintCenter;

public class PrintFailureFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task PrintSelectedRows_WhenPrintFails_MarksJobFailedAndDoesNotMarkImportRowPrinted()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Normal);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 1);
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var printer = new FakePrintExecutor { ThrowOnPrint = true };
        var service = new LabelPrintService(database.Settings, printer);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrintSelectedRowsAsync(template.Id, batch.Id, [importRow.Id], printerName: null));

        var job = await AppDb.Db.Queryable<LabelPrintJob>().SingleAsync();
        var savedRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);

        Assert.Equal("fake print failure", ex.Message);
        Assert.Equal("Failed", job.Status);
        Assert.Equal("fake print failure", job.ErrorMessage);
        Assert.False(savedRow.IsPrinted);
        Assert.Equal(0, savedRow.PrintCount);
    }
}
