using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Tests.Infrastructure;

public static class TestSeed
{
    public static async Task<LabelCategory> CategoryAsync(string name = "默认分类")
    {
        var category = new LabelCategory
        {
            Id = IdHelper.NewId(),
            Name = name,
            Sort = 1,
            IsEnabled = true,
            CreateTime = DateTime.Now
        };

        await AppDb.Db.Insertable(category).ExecuteCommandAsync();
        return category;
    }

    public static async Task<LabelTemplate> TemplateAsync(
        long categoryId,
        LabelTemplateMode mode,
        string name = "测试模板",
        string? batchPattern = null,
        string? serialPattern = null)
    {
        var template = new LabelTemplate
        {
            Id = IdHelper.NewId(),
            CategoryId = categoryId,
            Name = name,
            TemplateMode = mode,
            BatchNumberPattern = batchPattern ?? "BATCH-{yyyy}{MM}{dd}",
            SerialNumberPattern = serialPattern ?? "SN-{seq:0000}",
            DataSourceName = "LabelData",
            StorageType = TemplateStorageType.LocalFile,
            TemplatePath = "unused.mrt",
            TemplateFileName = "unused.mrt",
            Version = 1,
            IsEnabled = true,
            CreateTime = DateTime.Now
        };

        await AppDb.Db.Insertable(template).ExecuteCommandAsync();
        return template;
    }

    public static async Task<LabelTemplateField> FieldAsync(
        long templateId,
        string code,
        string name,
        int sort,
        bool required = false)
    {
        var field = new LabelTemplateField
        {
            Id = IdHelper.NewId(),
            TemplateId = templateId,
            FieldCode = code,
            FieldName = name,
            FieldType = "string",
            IsRequired = required,
            Sort = sort
        };

        await AppDb.Db.Insertable(field).ExecuteCommandAsync();
        return field;
    }

    public static async Task<LabelImportBatch> ImportBatchAsync(LabelTemplate template, int totalRows = 1)
    {
        var batch = new LabelImportBatch
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            TemplateName = template.Name,
            TemplateVersion = template.Version,
            ExcelFileName = "test.xlsx",
            TotalRows = totalRows,
            ValidRows = totalRows,
            InvalidRows = 0,
            Status = "Imported",
            OperatorName = "test",
            ImportTime = DateTime.Now
        };

        await AppDb.Db.Insertable(batch).ExecuteCommandAsync();
        return batch;
    }

    public static async Task<LabelImportRow> ImportRowAsync(
        LabelImportBatch batch,
        int rowIndex,
        Dictionary<string, string> data,
        bool isValid = true)
    {
        var rowDataJson = JsonHelper.Serialize(data);
        var row = new LabelImportRow
        {
            Id = IdHelper.NewId(),
            BatchId = batch.Id,
            TemplateId = batch.TemplateId,
            RowIndex = rowIndex,
            RowDataJson = rowDataJson,
            SearchText = SearchTextBuilder.FromJson(rowDataJson),
            IsValid = isValid,
            CreateTime = DateTime.Now
        };

        await AppDb.Db.Insertable(row).ExecuteCommandAsync();
        return row;
    }

    public static async Task<LabelPrintJob> FailedPrintJobAsync(LabelTemplate template, LabelImportBatch batch)
    {
        var job = new LabelPrintJob
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            BatchId = batch.Id,
            TemplateName = template.Name,
            SelectedRowCount = 1,
            Status = "Failed",
            ErrorMessage = "test failure",
            OperatorName = "test",
            CreateTime = DateTime.Now
        };

        await AppDb.Db.Insertable(job).ExecuteCommandAsync();
        return job;
    }

    public static async Task<LabelPrintJobRow> PrintJobRowAsync(
        LabelPrintJob job,
        LabelImportRow importRow,
        Dictionary<string, string> data)
    {
        var rowDataJson = JsonHelper.Serialize(data);
        var row = new LabelPrintJobRow
        {
            Id = IdHelper.NewId(),
            PrintJobId = job.Id,
            ImportRowId = importRow.Id,
            RowIndex = importRow.RowIndex,
            RowDataJson = rowDataJson,
            SearchText = SearchTextBuilder.FromJson(rowDataJson)
        };

        await AppDb.Db.Insertable(row).ExecuteCommandAsync();
        return row;
    }
}
