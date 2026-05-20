using ClosedXML.Excel;
using LabelPrintClient.Database;
using LabelPrintClient.Models;

namespace LabelPrintClient.Services.Excel;

public class ExcelTemplateExportService
{
    public void Export(long templateId, string savePath)
    {
        var fields = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId)
            .OrderBy(x => x.Sort)
            .ToList();

        if (fields.Count == 0)
            throw new InvalidOperationException("当前模板没有维护字段，无法生成 Excel 模板。");

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("导入数据");

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var col = i + 1;
            var headerCell = sheet.Cell(1, col);
            headerCell.Value = field.IsRequired ? $"{field.FieldName} *" : field.FieldName;
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Fill.BackgroundColor = XLColor.LightGray;

            sheet.Cell(2, col).Value = BuildExample(field);
            sheet.Cell(3, col).Value = field.Remark ?? string.Empty;
        }

        sheet.Row(2).Style.Font.Italic = true;
        sheet.Row(3).Style.Font.FontColor = XLColor.Gray;
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(savePath);
    }

    private static string BuildExample(LabelTemplateField field)
    {
        return field.FieldType.ToLowerInvariant() switch
        {
            "int" => "示例：10",
            "decimal" => "示例：12.5",
            "date" => "示例：2026-05-20",
            "bool" => "示例：true",
            _ => $"示例：{field.FieldName}"
        };
    }
}
