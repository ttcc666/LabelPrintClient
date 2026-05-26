using ClosedXML.Excel;
using LabelPrintClient.Database;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public class ExcelTemplateExportService
{
    private const int DataValidationMaxRow = 5000;

    public async Task ExportAsync(long templateId, string savePath, CancellationToken cancellationToken = default)
    {
        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .ToListAsync()
            .ConfigureAwait(false);

        if (fields.Count == 0)
            throw new InvalidOperationException("当前模板没有维护字段，无法生成 Excel 模板。");

        var exportFields = fields
            .Where(x => !TemplateSystemFields.IsSystemField(x.FieldCode))
            .ToList();
        if (exportFields.Count == 0)
            throw new InvalidOperationException("当前模板没有可导入字段，无法生成 Excel 模板。");

        await Task.Run(() => SaveWorkbook(exportFields, savePath), cancellationToken).ConfigureAwait(false);
    }

    private static void SaveWorkbook(IReadOnlyList<LabelTemplateField> fields, string savePath)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("导入数据");
        var instructionSheet = workbook.Worksheets.Add("字段说明");
        var enumFields = fields
            .Select((Field, Index) => new { Field, Index })
            .Select(x => new EnumFieldSource(x.Field, x.Index, ParseEnumOptions(x.Field.EnumOptions)))
            .Where(x => x.Values.Count > 0)
            .ToList();
        var enumSheet = enumFields.Count > 0
            ? workbook.Worksheets.Add("枚举数据源")
            : null;

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var col = i + 1;
            var headerCell = sheet.Cell(1, col);
            headerCell.Value = field.IsRequired ? $"{field.FieldName} *" : field.FieldName;
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        if (enumSheet != null)
        {
            BuildEnumSourceSheet(enumSheet, enumFields);
            ApplyEnumDataValidations(sheet, enumSheet, enumFields);
            enumSheet.Visibility = XLWorksheetVisibility.VeryHidden;
        }

        BuildInstructionSheet(instructionSheet, fields);
        ApplyDataSheetStyle(sheet, fields.Count);
        ApplyInstructionSheetStyle(instructionSheet, fields.Count + 1);
        workbook.SaveAs(savePath);
    }

    private static void BuildInstructionSheet(IXLWorksheet sheet, IReadOnlyList<LabelTemplateField> fields)
    {
        var headers = new[] { "字段名称", "字段编码", "类型", "是否必填", "示例", "枚举值", "备注" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = field.FieldName;
            sheet.Cell(row, 2).Value = field.FieldCode;
            sheet.Cell(row, 3).Value = field.FieldType;
            sheet.Cell(row, 4).Value = field.IsRequired ? "是" : "否";
            sheet.Cell(row, 5).Value = BuildExample(field);
            sheet.Cell(row, 6).Value = BuildEnumInstruction(field.EnumOptions);
            sheet.Cell(row, 7).Value = field.Remark ?? string.Empty;
        }
    }

    private static void BuildEnumSourceSheet(
        IXLWorksheet sheet,
        IReadOnlyList<EnumFieldSource> enumFields)
    {
        for (var i = 0; i < enumFields.Count; i++)
        {
            var col = i + 1;
            sheet.Cell(1, col).Value = enumFields[i].Field.FieldName;
            sheet.Cell(1, col).Style.Font.Bold = true;

            for (var rowIndex = 0; rowIndex < enumFields[i].Values.Count; rowIndex++)
            {
                sheet.Cell(rowIndex + 2, col).Value = enumFields[i].Values[rowIndex];
            }

            sheet.Column(col).AdjustToContents();
        }
    }

    private static void ApplyEnumDataValidations(
        IXLWorksheet dataSheet,
        IXLWorksheet enumSheet,
        IReadOnlyList<EnumFieldSource> enumFields)
    {
        for (var i = 0; i < enumFields.Count; i++)
        {
            var item = enumFields[i];
            var dataColumn = item.FieldIndex + 1;
            var enumColumn = i + 1;
            var enumRange = enumSheet.Range(2, enumColumn, item.Values.Count + 1, enumColumn);
            var validation = dataSheet.Range(2, dataColumn, DataValidationMaxRow, dataColumn).CreateDataValidation();
            validation.List(enumRange);
            validation.IgnoreBlanks = !item.Field.IsRequired;
            validation.InCellDropdown = true;
            validation.ShowErrorMessage = true;
            validation.ErrorTitle = "输入不在枚举范围";
            validation.ErrorMessage = "请从下拉列表选择允许值。";
        }
    }

    private static void ApplyDataSheetStyle(IXLWorksheet sheet, int columnCount)
    {
        var headerRange = sheet.Range(1, 1, 1, columnCount);
        ApplyHeaderStyle(headerRange);
        headerRange.SetAutoFilter();
        sheet.SheetView.FreezeRows(1);

        AdjustColumns(sheet, columnCount, 14, 36);
    }

    private static void ApplyInstructionSheetStyle(IXLWorksheet sheet, int rowCount)
    {
        ApplyHeaderStyle(sheet.Range(1, 1, 1, 7));

        var usedRange = sheet.Range(1, 1, rowCount, 7);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        sheet.SheetView.FreezeRows(1);
        AdjustColumns(sheet, 7, 10, 48);
        sheet.Column(6).Style.Alignment.WrapText = true;
        sheet.Column(7).Style.Alignment.WrapText = true;
    }

    private static void ApplyHeaderStyle(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static void AdjustColumns(IXLWorksheet sheet, int columnCount, double minWidth, double maxWidth)
    {
        for (var col = 1; col <= columnCount; col++)
        {
            var column = sheet.Column(col);
            column.AdjustToContents();
            if (column.Width < minWidth) column.Width = minWidth;
            if (column.Width > maxWidth) column.Width = maxWidth;
        }
    }

    private static string BuildExample(LabelTemplateField field)
    {
        var enumValues = ParseEnumOptions(field.EnumOptions);
        if (enumValues.Count > 0)
            return $"示例：{enumValues[0]}";

        return field.FieldType.ToLowerInvariant() switch
        {
            "int" => "示例：10",
            "decimal" => "示例：12.5",
            "date" => "示例：2026-05-20",
            "bool" => "示例：true",
            _ => $"示例：{field.FieldName}"
        };
    }

    private static List<string> ParseEnumOptions(string? enumOptions)
    {
        if (string.IsNullOrWhiteSpace(enumOptions))
            return new List<string>();

        return enumOptions
            .Split(new[] { "\r\n", "\n", ",", "，", ";", "；" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildEnumInstruction(string? enumOptions)
    {
        var values = ParseEnumOptions(enumOptions);
        return values.Count == 0 ? string.Empty : string.Join(Environment.NewLine, values);
    }
    private sealed record EnumFieldSource(LabelTemplateField Field, int FieldIndex, List<string> Values);
}
