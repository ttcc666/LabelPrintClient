using LabelPrintClient.Database;
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
}
