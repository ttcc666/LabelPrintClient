using System.Data;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;

namespace LabelPrintClient.Services.Print;

public static class DataTableBuilder
{
    public static DataTable Build(
        IEnumerable<LabelImportRow> rows,
        IReadOnlyList<LabelTemplateField> fields,
        string dataSourceName)
    {
        var table = new DataTable(dataSourceName);

        foreach (var field in fields)
        {
            if (!table.Columns.Contains(field.FieldCode))
                table.Columns.Add(field.FieldCode, typeof(string));
        }

        foreach (var row in rows)
        {
            var dict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>();
            var dataRow = table.NewRow();
            foreach (var field in fields)
            {
                dataRow[field.FieldCode] = dict.TryGetValue(field.FieldCode, out var value) ? value : string.Empty;
            }
            table.Rows.Add(dataRow);
        }

        return table;
    }
}
