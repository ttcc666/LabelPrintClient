using System.IO;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using LabelPrintClient.Services.Excel;

namespace LabelPrintClient.Services.Import;

public class LabelImportService
{
    public async Task<long> ImportExcelAsync(
        long templateId,
        string excelPath,
        string? operatorName,
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
            .Where(x => x.TemplateId == templateId)
            .OrderBy(x => x.Sort)
            .ToListAsync()
            .ConfigureAwait(false);

        if (fields.Count == 0)
            throw new InvalidOperationException("当前模板没有维护字段，不能导入 Excel。");

        var drafts = await ExcelReader.ReadRowsAsync(excelPath, fields, cancellationToken).ConfigureAwait(false);
        if (drafts.Count == 0)
            throw new InvalidOperationException("Excel 中没有可导入的数据行。");

        var fileHash = await FileHashHelper.GetSha256Async(excelPath, cancellationToken).ConfigureAwait(false);
        var batch = BuildBatch(template, excelPath, fileHash, drafts, operatorName);
        var rows = BuildRows(template.Id, batch.Id, drafts);

        await AppDb.Db.Ado.BeginTranAsync().ConfigureAwait(false);
        try
        {
            await AppDb.Db.Insertable(batch).ExecuteCommandAsync().ConfigureAwait(false);
            await AppDb.Db.Insertable(rows).ExecuteCommandAsync().ConfigureAwait(false);
            await AppDb.Db.Ado.CommitTranAsync().ConfigureAwait(false);
        }
        catch
        {
            await AppDb.Db.Ado.RollbackTranAsync().ConfigureAwait(false);
            throw;
        }

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
