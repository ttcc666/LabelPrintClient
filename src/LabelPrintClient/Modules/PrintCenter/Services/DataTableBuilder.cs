using System.Data;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public static class DataTableBuilder
{
    public static DataTable Build(
        IEnumerable<LabelImportRow> rows,
        IReadOnlyList<LabelTemplateField> fields,
        string dataSourceName,
        int copyCount = 1)
    {
        copyCount = Math.Max(1, copyCount);
        var table = new DataTable(dataSourceName);
        var rowData = rows
            .Select(row => JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>())
            .ToList();

        foreach (var field in fields)
        {
            AddColumn(table, field.FieldCode);
        }

        foreach (var dict in rowData)
        {
            foreach (var key in dict.Keys)
            {
                if (IsLegacySystemAlias(key))
                    continue;

                AddColumn(table, key);
            }
        }

        AddLegacySystemCompatibilityColumns(table);

        foreach (var dict in rowData)
        {
            for (var copyIndex = 0; copyIndex < copyCount; copyIndex++)
            {
                var dataRow = table.NewRow();
                foreach (var field in fields)
                {
                    SetValue(dataRow, field.FieldCode, dict.TryGetValue(field.FieldCode, out var value) ? value : string.Empty);
                }

                foreach (var (key, value) in dict)
                {
                    if (IsLegacySystemAlias(key))
                        continue;

                    SetValue(dataRow, key, value);
                }

                table.Rows.Add(dataRow);
            }
        }

        return table;
    }

    private static void AddColumn(DataTable table, string? columnName)
    {
        if (!string.IsNullOrWhiteSpace(columnName) && !table.Columns.Contains(columnName))
            table.Columns.Add(columnName, typeof(string)).DefaultValue = string.Empty;
    }

    private static void SetValue(DataRow row, string columnName, string? value)
    {
        if (row.Table.Columns.Contains(columnName))
            row[columnName] = value ?? string.Empty;
    }

    private static bool IsLegacySystemAlias(string key)
    {
        return string.Equals(key, "batch_no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "serial_no", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddLegacySystemCompatibilityColumns(DataTable table)
    {
        if (!table.Columns.Contains(TemplateSystemFields.BatchNo))
            AddColumn(table, "_batch_no");

        if (!table.Columns.Contains(TemplateSystemFields.SerialNo))
            AddColumn(table, "_serial_no");
    }
}
