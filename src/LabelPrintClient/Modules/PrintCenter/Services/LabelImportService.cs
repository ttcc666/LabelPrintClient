using System.IO;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.PrintCenter.Services;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public class LabelImportService
{
    public async Task<long> ImportExcelAsync(
        long templateId,
        string excelPath,
        string? operatorName,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewExcelAsync(templateId, excelPath, cancellationToken).ConfigureAwait(false);
        return await CommitImportAsync(preview, operatorName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImportPreviewResult> PreviewExcelAsync(
        long templateId,
        string excelPath,
        CancellationToken cancellationToken = default)
    {
        var templates = await AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.Id == templateId)
            .Take(1)
            .ToListAsync()
            .ConfigureAwait(false);
        var template = templates.FirstOrDefault()
            ?? throw new InvalidOperationException("模板不存在。");

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .ToListAsync()
            .ConfigureAwait(false);

        if (fields.Count == 0)
            throw new InvalidOperationException("当前模板没有维护字段，不能导入 Excel。");

        var drafts = await ExcelReader.ReadRowsAsync(excelPath, fields, cancellationToken).ConfigureAwait(false);
        if (drafts.Count == 0)
            throw new InvalidOperationException("Excel 中没有可导入的数据行。");

        var fileHash = await FileHashHelper.GetSha256Async(excelPath, cancellationToken).ConfigureAwait(false);
        return new ImportPreviewResult
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            TemplateVersion = template.Version,
            ExcelPath = excelPath,
            ExcelFileName = Path.GetFileName(excelPath),
            ExcelFileHash = fileHash,
            Fields = fields,
            Rows = drafts
        };
    }

    public async Task<long> CommitImportAsync(
        ImportPreviewResult preview,
        string? operatorName,
        CancellationToken cancellationToken = default)
    {
        if (preview.Rows.Count == 0)
            throw new InvalidOperationException("没有可导入的数据行。");

        if (preview.Rows.Any(x => !x.IsValid))
            throw new InvalidOperationException("存在错误行，不能确认导入。请修正 Excel 后重新导入。");

        cancellationToken.ThrowIfCancellationRequested();

        var template = new LabelTemplate
        {
            Id = preview.TemplateId,
            Name = preview.TemplateName,
            Version = preview.TemplateVersion
        };
        var batch = BuildBatch(template, preview.ExcelPath, preview.ExcelFileHash, preview.Rows, operatorName);
        var rows = BuildRows(preview.TemplateId, batch.Id, preview.Rows);

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Insertable(batch).ExecuteCommandAsync().ConfigureAwait(false);
            await AppDb.Db.Insertable(rows).ExecuteCommandAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        return batch.Id;
    }

    private static LabelImportBatch BuildBatch(
        LabelTemplate template,
        string excelPath,
        string? fileHash,
        IReadOnlyList<ImportRowDraft> drafts,
        string? operatorName)
    {
        return new LabelImportBatch
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            TemplateName = template.Name,
            TemplateVersion = template.Version,
            ExcelFileName = Path.GetFileName(excelPath),
            ExcelFileHash = fileHash,
            TotalRows = drafts.Count,
            ValidRows = drafts.Count(x => x.IsValid),
            InvalidRows = drafts.Count(x => !x.IsValid),
            Status = drafts.Any(x => !x.IsValid) ? "PartError" : "Imported",
            OperatorName = operatorName,
            ImportTime = DateTime.Now
        };
    }

    private static List<LabelImportRow> BuildRows(long templateId, long batchId, IEnumerable<ImportRowDraft> drafts)
    {
        return drafts.Select(x => new LabelImportRow
        {
            Id = IdHelper.NewId(),
            BatchId = batchId,
            TemplateId = templateId,
            RowIndex = x.RowIndex,
            RowDataJson = JsonHelper.Serialize(x.Data),
            IsValid = x.IsValid,
            ErrorMessage = x.IsValid ? null : x.ErrorMessage,
            IsPrinted = false,
            PrintCount = 0,
            CreateTime = DateTime.Now
        }).ToList();
    }
}
