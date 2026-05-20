using System.IO;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using LabelPrintClient.Services.Excel;

namespace LabelPrintClient.Services.Import;

public class LabelImportService
{
    public long ImportExcel(long templateId, string excelPath, string? operatorName)
    {
        var template = AppDb.Db.Queryable<LabelTemplate>().First(x => x.Id == templateId)
            ?? throw new InvalidOperationException("模板不存在。");

        var fields = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId)
            .OrderBy(x => x.Sort)
            .ToList();

        if (fields.Count == 0)
            throw new InvalidOperationException("当前模板没有维护字段，不能导入 Excel。");

        var drafts = ExcelReader.ReadRows(excelPath, fields);
        if (drafts.Count == 0)
            throw new InvalidOperationException("Excel 中没有可导入的数据行。");

        var batch = new LabelImportBatch
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            TemplateName = template.Name,
            TemplateVersion = template.Version,
            ExcelFileName = Path.GetFileName(excelPath),
            ExcelFileHash = FileHashHelper.GetSha256(excelPath),
            TotalRows = drafts.Count,
            ValidRows = drafts.Count(x => x.IsValid),
            InvalidRows = drafts.Count(x => !x.IsValid),
            Status = drafts.Any(x => !x.IsValid) ? "PartError" : "Imported",
            OperatorName = operatorName,
            ImportTime = DateTime.Now
        };

        var rows = drafts.Select(x => new LabelImportRow
        {
            Id = IdHelper.NewId(),
            BatchId = batch.Id,
            TemplateId = template.Id,
            RowIndex = x.RowIndex,
            RowDataJson = JsonHelper.Serialize(x.Data),
            IsValid = x.IsValid,
            ErrorMessage = x.IsValid ? null : x.ErrorMessage,
            IsPrinted = false,
            PrintCount = 0,
            CreateTime = DateTime.Now
        }).ToList();

        AppDb.Db.Ado.UseTran(() =>
        {
            AppDb.Db.Insertable(batch).ExecuteCommand();
            AppDb.Db.Insertable(rows).ExecuteCommand();
        });

        return batch.Id;
    }
}
