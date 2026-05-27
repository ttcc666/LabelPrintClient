using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.Template.Services;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.Template;

public class TemplateModeFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task EnsureModeFieldsAsync_NormalTemplate_DoesNotCreateSystemField()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Normal);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 1);

        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && !x.IsDeleted)
            .ToListAsync();

        Assert.Single(fields);
        Assert.Equal("ProductCode", fields.Single().FieldCode);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task EnsureModeFieldsAsync_BatchTemplate_CreatesBatchSystemField()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);

        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        var field = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && x.FieldCode == TemplateSystemFields.BatchNo)
            .SingleAsync();

        Assert.Equal("批号", field.FieldName);
        Assert.True(field.IsRequired);
        Assert.False(field.IsDeleted);
        Assert.Equal("string", field.FieldType);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task EnsureModeFieldsAsync_ModeSwitch_ReplacesManagedSystemField()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        template.TemplateMode = LabelTemplateMode.Serialized;
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .ToListAsync();
        var batchField = fields.Single(x => x.FieldCode == TemplateSystemFields.BatchNo);
        var serialField = fields.Single(x => x.FieldCode == TemplateSystemFields.SerialNo);

        Assert.True(batchField.IsDeleted);
        Assert.False(serialField.IsDeleted);
        Assert.Equal("序列号", serialField.FieldName);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task EnsureModeFieldsAsync_NormalMode_SoftDeletesManagedSystemFields()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        template.TemplateMode = LabelTemplateMode.Normal;
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        var batchField = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && x.FieldCode == TemplateSystemFields.BatchNo)
            .SingleAsync();

        Assert.True(batchField.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task EnsureModeFieldsAsync_ReactivatesExistingDeletedSystemField()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        var field = await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "旧批号", 0);
        field.IsDeleted = true;
        field.IsRequired = false;
        field.FieldType = "int";
        await AppDb.Db.Updateable(field).ExecuteCommandAsync();

        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        var restored = await AppDb.Db.Queryable<LabelTemplateField>().InSingleAsync(field.Id);

        Assert.False(restored.IsDeleted);
        Assert.True(restored.IsRequired);
        Assert.Equal("string", restored.FieldType);
        Assert.Equal("批号", restored.FieldName);
        Assert.Equal(5, restored.Sort);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SwitchingAcrossNormalBatchSerialized_MaintainsExpectedSystemFieldsAndBatchBindings()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Normal);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 1);

        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);
        await AssertActiveSystemFieldsAsync(template.Id);

        template.TemplateMode = LabelTemplateMode.Batch;
        template.BatchNumberPattern = "BATCH-{ProductCode}";
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);
        await AssertActiveSystemFieldsAsync(template.Id, TemplateSystemFields.BatchNo);

        var batch = await TestSeed.ImportBatchAsync(template);
        var importRow = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-P001"
        });
        importRow.IsPrinted = true;
        importRow.PrintCount = 1;
        await AppDb.Db.Updateable(importRow).ExecuteCommandAsync();
        var job = await TestSeed.FailedPrintJobAsync(template, batch);
        job.Status = "Printed";
        await AppDb.Db.Updateable(job).ExecuteCommandAsync();
        var jobRow = await TestSeed.PrintJobRowAsync(job, importRow, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001",
            [TemplateSystemFields.BatchNo] = "BATCH-HISTORY"
        });

        template.TemplateMode = LabelTemplateMode.Serialized;
        template.BatchNumberPattern = null;
        template.SerialNumberPattern = "SN-{seq:0000}";
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);
        var clearedCount = await TemplateBatchBindingService.ClearLockedBatchNumbersAsync(template.Id);
        await AssertActiveSystemFieldsAsync(template.Id, TemplateSystemFields.SerialNo);

        var switchedImportRow = await AppDb.Db.Queryable<LabelImportRow>().InSingleAsync(importRow.Id);
        var switchedImportData = JsonHelper.Deserialize<Dictionary<string, string>>(switchedImportRow.RowDataJson)!;
        var savedJobRow = await AppDb.Db.Queryable<LabelPrintJobRow>().InSingleAsync(jobRow.Id);
        var historyData = JsonHelper.Deserialize<Dictionary<string, string>>(savedJobRow.RowDataJson)!;

        Assert.Equal(1, clearedCount);
        Assert.DoesNotContain(TemplateSystemFields.BatchNo, switchedImportData.Keys);
        Assert.Equal("BATCH-HISTORY", historyData[TemplateSystemFields.BatchNo]);

        template.TemplateMode = LabelTemplateMode.Normal;
        template.SerialNumberPattern = null;
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);
        await AssertActiveSystemFieldsAsync(template.Id);

        template.TemplateMode = LabelTemplateMode.Batch;
        template.BatchNumberPattern = "BATCH-NEW-{ProductCode}";
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);
        await AssertActiveSystemFieldsAsync(template.Id, TemplateSystemFields.BatchNo);

        var batchField = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && x.FieldCode == TemplateSystemFields.BatchNo)
            .SingleAsync();
        var serialField = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && x.FieldCode == TemplateSystemFields.SerialNo)
            .SingleAsync();

        Assert.False(batchField.IsDeleted);
        Assert.True(serialField.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SwitchingAwayFromBatchAndBack_PreservesBatchRule()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Batch,
            batchPattern: "LOT-{ProductCode}-{yyyy}{MM}{dd}");

        template.TemplateMode = LabelTemplateMode.Normal;
        await AppDb.Db.Updateable(template).ExecuteCommandAsync();
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        template.TemplateMode = LabelTemplateMode.Batch;
        await AppDb.Db.Updateable(template).ExecuteCommandAsync();
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        var savedTemplate = await AppDb.Db.Queryable<LabelTemplate>().InSingleAsync(template.Id);

        Assert.Equal("LOT-{ProductCode}-{yyyy}{MM}{dd}", savedTemplate.BatchNumberPattern);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SwitchingAwayFromSerializedAndBack_PreservesSerialRuleAndResetPeriod()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Serialized,
            serialPattern: "SER-{ProductCode}-{seq:000}");
        template.SerialResetPeriod = SerialResetPeriod.Monthly;
        await AppDb.Db.Updateable(template).ExecuteCommandAsync();

        template.TemplateMode = LabelTemplateMode.Normal;
        await AppDb.Db.Updateable(template).ExecuteCommandAsync();
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        template.TemplateMode = LabelTemplateMode.Serialized;
        await AppDb.Db.Updateable(template).ExecuteCommandAsync();
        await TemplateSystemFieldService.EnsureModeFieldsAsync(template);

        var savedTemplate = await AppDb.Db.Queryable<LabelTemplate>().InSingleAsync(template.Id);

        Assert.Equal("SER-{ProductCode}-{seq:000}", savedTemplate.SerialNumberPattern);
        Assert.Equal(SerialResetPeriod.Monthly, savedTemplate.SerialResetPeriod);
    }

    private static async Task AssertActiveSystemFieldsAsync(long templateId, params string[] expectedCodes)
    {
        var activeSystemCodes = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId && !x.IsDeleted)
            .ToListAsync();
        activeSystemCodes = activeSystemCodes
            .Where(x => TemplateSystemFields.IsManagedSystemField(x.FieldCode))
            .OrderBy(x => x.FieldCode)
            .ToList();

        Assert.Equal(
            expectedCodes.OrderBy(x => x).ToList(),
            activeSystemCodes.Select(x => x.FieldCode).ToList());
    }
}
