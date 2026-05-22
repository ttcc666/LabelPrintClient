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

        foreach (var field in fields)
        {
            if (!table.Columns.Contains(field.FieldCode))
                table.Columns.Add(field.FieldCode, typeof(string));
        }

        foreach (var row in rows)
        {
            var dict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>();
            for (var copyIndex = 0; copyIndex < copyCount; copyIndex++)
            {
                var dataRow = table.NewRow();
                foreach (var field in fields)
                {
                    dataRow[field.FieldCode] = dict.TryGetValue(field.FieldCode, out var value) ? value : string.Empty;
                }
                table.Rows.Add(dataRow);
            }
        }

        return table;
    }
}