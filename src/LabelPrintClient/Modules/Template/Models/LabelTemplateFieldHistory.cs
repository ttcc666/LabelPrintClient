using System.Text.Json;
using SqlSugar;

namespace LabelPrintClient.Modules.Template.Models;

[SugarTable("label_template_field_history")]
public class LabelTemplateFieldHistory
{
    public const string OperationUpdate = "Update";
    public const string OperationDelete = "Delete";

    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TemplateId { get; set; }

    [SugarColumn(Length = 100)]
    public string TemplateName { get; set; } = string.Empty;

    public long FieldId { get; set; }

    [SugarColumn(Length = 20)]
    public string OperationType { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "text")]
    public string BeforeSnapshotJson { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? AfterSnapshotJson { get; set; }

    [SugarColumn(ColumnDataType = "text")]
    public string ChangeSummary { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? OperatorName { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [SugarColumn(IsIgnore = true)]
    public string OperationTypeText => OperationType switch
    {
        OperationUpdate => "变更",
        OperationDelete => "删除",
        _ => OperationType
    };

    [SugarColumn(IsIgnore = true)]
    public string FieldName => ReadSnapshotValue(nameof(LabelTemplateField.FieldName));

    [SugarColumn(IsIgnore = true)]
    public string FieldCode => ReadSnapshotValue(nameof(LabelTemplateField.FieldCode));

    private string ReadSnapshotValue(string propertyName)
    {
        var json = string.IsNullOrWhiteSpace(AfterSnapshotJson)
            ? BeforeSnapshotJson
            : AfterSnapshotJson;

        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out var value)
                ? value.ToString()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
