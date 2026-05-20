using SqlSugar;

namespace LabelPrintClient.Models;

[SugarTable("label_template_field")]
public class LabelTemplateField
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TemplateId { get; set; }

    [SugarColumn(Length = 100)]
    public string FieldName { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string FieldCode { get; set; } = string.Empty;

    /// <summary>
    /// 支持 string / int / decimal / date / bool
    /// </summary>
    [SugarColumn(Length = 50)]
    public string FieldType { get; set; } = "string";

    public bool IsRequired { get; set; }

    public int Sort { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Remark { get; set; }
}
