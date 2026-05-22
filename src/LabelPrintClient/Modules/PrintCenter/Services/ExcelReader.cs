using ClosedXML.Excel;
using System.Text.RegularExpressions;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public static class ExcelReader
{
    public static Task<List<ImportRowDraft>> ReadRowsAsync(
        string filePath,
        IReadOnlyList<LabelTemplateField> fields,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadRows(filePath, fields), cancellationToken);
    }

    public static List<ImportRowDraft> ReadRows(string filePath, IReadOnlyList<LabelTemplateField> fields)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);
        var result = new List<ImportRowDraft>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

        var headerMap = BuildHeaderMap(sheet);

        for (var rowIndex = 2; rowIndex <= lastRow; rowIndex++)
        {
            var draft = new ImportRowDraft { RowIndex = rowIndex };

            var isEmptyRow = true;
            foreach (var field in fields)
            {
                if (!headerMap.TryGetValue(field.FieldName, out var colIndex))
                {
                    draft.Data[field.FieldCode] = string.Empty;
                    if (field.IsRequired) draft.Errors.Add($"缺少必填列：{field.FieldName}");
                    continue;
                }

                var cell = sheet.Cell(rowIndex, colIndex);
                var value = ReadCellAsString(cell);
                if (!string.IsNullOrWhiteSpace(value)) isEmptyRow = false;

                if (field.IsRequired && string.IsNullOrWhiteSpace(value))
                {
                    draft.Errors.Add($"字段【{field.FieldName}】不能为空");
                }

                var normalizedValue = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string? validatedValue;
                    string? error;
                    if (!ValidateValue(field.FieldType, value, out validatedValue, out error))
                    {
                        draft.Errors.Add($"字段【{field.FieldName}】{error}");
                    }
                    else
                    {
                        normalizedValue = validatedValue ?? value;
                        ValidateFieldRules(field, normalizedValue, draft.Errors);
                    }
                }

                draft.Data[field.FieldCode] = normalizedValue;
            }

            if (!isEmptyRow) result.Add(draft);
        }

        return result;
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = sheet.Row(1);
        var lastCell = headerRow.LastCellUsed();
        if (lastCell == null) return map;

        for (var col = 1; col <= lastCell.Address.ColumnNumber; col++)
        {
            var header = headerRow.Cell(col).GetString().Replace("*", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(header) && !map.ContainsKey(header))
            {
                map[header] = col;
            }
        }

        return map;
    }

    private static string ReadCellAsString(IXLCell cell)
    {
        if (cell.IsEmpty()) return string.Empty;
        return cell.GetFormattedString().Trim();
    }

    private static bool ValidateValue(string fieldType, string value, out string? normalizedValue, out string? error)
    {
        normalizedValue = value;
        error = null;

        switch (fieldType.Trim().ToLowerInvariant())
        {
            case "int":
                if (!int.TryParse(value, out var intValue))
                {
                    error = "必须是整数";
                    return false;
                }
                normalizedValue = intValue.ToString();
                return true;

            case "decimal":
                if (!decimal.TryParse(value, out var decimalValue))
                {
                    error = "必须是数字";
                    return false;
                }
                normalizedValue = decimalValue.ToString("0.################");
                return true;

            case "date":
                if (!DateTime.TryParse(value, out var dateValue))
                {
                    error = "必须是日期";
                    return false;
                }
                normalizedValue = dateValue.ToString("yyyy-MM-dd");
                return true;

            case "bool":
                if (!bool.TryParse(value, out var boolValue))
                {
                    if (value is "是" or "1")
                    {
                        normalizedValue = "true";
                        return true;
                    }
                    if (value is "否" or "0")
                    {
                        normalizedValue = "false";
                        return true;
                    }
                    error = "必须是 true/false、是/否 或 1/0";
                    return false;
                }
                normalizedValue = boolValue.ToString().ToLowerInvariant();
                return true;

            default:
                return true;
        }
    }

    private static void ValidateFieldRules(LabelTemplateField field, string value, ICollection<string> errors)
    {
        if (field.MinLength.HasValue && value.Length < field.MinLength.Value)
            errors.Add($"字段【{field.FieldName}】长度不能小于 {field.MinLength.Value}");

        if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
            errors.Add($"字段【{field.FieldName}】长度不能大于 {field.MaxLength.Value}");

        var enumValues = ParseEnumOptions(field.EnumOptions);
        if (enumValues.Count > 0 && !enumValues.Contains(value))
            errors.Add($"字段【{field.FieldName}】不在允许值范围内");

        if ((field.MinValue.HasValue || field.MaxValue.HasValue) &&
            IsNumericField(field.FieldType) &&
            decimal.TryParse(value, out var numericValue))
        {
            if (field.MinValue.HasValue && numericValue < field.MinValue.Value)
                errors.Add($"字段【{field.FieldName}】不能小于 {field.MinValue.Value}");

            if (field.MaxValue.HasValue && numericValue > field.MaxValue.Value)
                errors.Add($"字段【{field.FieldName}】不能大于 {field.MaxValue.Value}");
        }

        if (string.IsNullOrWhiteSpace(field.RegexPattern))
            return;

        try
        {
            if (!Regex.IsMatch(value, field.RegexPattern, RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                var message = string.IsNullOrWhiteSpace(field.RegexErrorMessage)
                    ? "格式不符合要求"
                    : field.RegexErrorMessage;
                errors.Add($"字段【{field.FieldName}】{message}");
            }
        }
        catch (ArgumentException)
        {
            errors.Add($"字段【{field.FieldName}】正则规则无效");
        }
        catch (RegexMatchTimeoutException)
        {
            errors.Add($"字段【{field.FieldName}】正则校验超时");
        }
    }

    private static HashSet<string> ParseEnumOptions(string? enumOptions)
    {
        if (string.IsNullOrWhiteSpace(enumOptions))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return enumOptions
            .Split(new[] { "\r\n", "\n", ",", "，", ";", "；" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsNumericField(string fieldType)
    {
        return fieldType.Trim().Equals("int", StringComparison.OrdinalIgnoreCase) ||
               fieldType.Trim().Equals("decimal", StringComparison.OrdinalIgnoreCase);
    }
}
