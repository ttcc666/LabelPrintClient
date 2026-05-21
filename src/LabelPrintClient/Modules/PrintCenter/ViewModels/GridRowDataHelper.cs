using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.ViewModels;

public static class GridRowDataHelper
{
    public static Dictionary<string, string> Deserialize(
        string? json,
        IEnumerable<LabelTemplateField>? fields = null)
    {
        var data = string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonHelper.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

        if (fields != null)
            EnsureFieldKeys(data, fields);

        return data;
    }

    public static Dictionary<string, string> Normalize(
        Dictionary<string, string>? data,
        IEnumerable<LabelTemplateField> fields)
    {
        var normalized = data == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(data);

        EnsureFieldKeys(normalized, fields);
        return normalized;
    }

    public static void EnsureFieldKeys(
        IEnumerable<Dictionary<string, string>> rows,
        IEnumerable<LabelTemplateField> fields)
    {
        var fieldCodes = fields
            .Select(x => x.FieldCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnsureKeys(rows, fieldCodes);
    }

    public static void EnsureKeys(
        IEnumerable<Dictionary<string, string>> rows,
        IEnumerable<string> keys)
    {
        var keyList = keys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var row in rows)
        {
            foreach (var key in keyList)
            {
                EnsureKey(row, key);
            }
        }
    }

    private static void EnsureFieldKeys(
        Dictionary<string, string> data,
        IEnumerable<LabelTemplateField> fields)
    {
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field.FieldCode))
                EnsureKey(data, field.FieldCode);
        }
    }

    private static void EnsureKey(Dictionary<string, string> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            data[key] = string.Empty;
    }
}

