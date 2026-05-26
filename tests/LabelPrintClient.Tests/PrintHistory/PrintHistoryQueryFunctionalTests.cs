using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.PrintHistory.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.PrintHistory;

public class PrintHistoryQueryFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task QueryJobsAsync_FiltersByStatusAndKeyword()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Normal, name: "感冒灵模板");
        var batch = await TestSeed.ImportBatchAsync(template);
        await InsertJobAsync(template, batch, "Printed", "HP-01", "alice", null, DateTime.Now.AddMinutes(-1));
        await InsertJobAsync(template, batch, "Failed", "Zebra-02", "bob", "纸张错误", DateTime.Now);
        var service = new PrintHistoryQueryService();

        var result = await service.QueryJobsAsync("Failed", "纸张", 1, 20);

        Assert.Single(result.Items);
        Assert.Equal("Failed", result.Items.Single().Status);
        Assert.Equal("纸张错误", result.Items.Single().ErrorMessage);
        Assert.Equal(1, result.TotalRows);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task QueryRowsAsync_ReturnsSystemAndDeprecatedKeys()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2);
        await TestSeed.FieldAsync(template.Id, "DeletedField", "旧字段", 3);
        var deletedField = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && x.FieldCode == "DeletedField")
            .SingleAsync();
        deletedField.IsDeleted = true;
        await AppDb.Db.Updateable(deletedField).ExecuteCommandAsync();
        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var job = await InsertJobAsync(template, batch, "Printed", null, "tester", null, DateTime.Now);
        await TestSeed.PrintJobRowAsync(job, importRow, new Dictionary<string, string>
        {
            [TemplateSystemFields.BatchNo] = "BATCH-001",
            ["ProductCode"] = "P001",
            ["DeletedField"] = "历史值"
        });
        var service = new PrintHistoryQueryService();

        var result = await service.QueryRowsAsync(job.Id, template.Id, "BATCH", 1, 50);

        Assert.Single(result.Rows.Items);
        Assert.Equal([TemplateSystemFields.BatchNo], result.SystemKeys);
        Assert.Equal(["DeletedField"], result.ExtraKeys);
        Assert.Equal("BATCH-001", result.Rows.Items.Single().Data[TemplateSystemFields.BatchNo]);
        Assert.Equal("历史值", result.Rows.Items.Single().Data["DeletedField"]);
    }

    private static async Task<LabelPrintJob> InsertJobAsync(
        LabelTemplate template,
        LabelImportBatch batch,
        string status,
        string? printerName,
        string? operatorName,
        string? errorMessage,
        DateTime createTime)
    {
        var job = new LabelPrintJob
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            BatchId = batch.Id,
            TemplateName = template.Name,
            SelectedRowCount = 1,
            PrinterName = printerName,
            Status = status,
            OperatorName = operatorName,
            ErrorMessage = errorMessage,
            CreateTime = createTime,
            PrintTime = status == "Printed" ? createTime : null
        };
        await AppDb.Db.Insertable(job).ExecuteCommandAsync();
        return job;
    }
}
