using SqlSugar;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Template.Models;

[SugarTable("label_template_field")]
[SugarIndex(
    "ix_label_template_field_template_sort",
    nameof(TemplateId), OrderByType.Asc,
    nameof(IsDeleted), OrderByType.Asc,
    nameof(Sort), OrderByType.Asc)]
[SugarIndex(
    "ix_label_template_field_template_code",
    nameof(TemplateId), OrderByType.Asc,
    nameof(FieldCode), OrderByType.Asc)]
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

    [SugarColumn(IsIgnore = true)]
    public string FieldTypeCn
    {
        get
        {
            return FieldType switch
            {
                "string" => AppLanguageService.GetString("Field.TypeStringShort"),
                "int" => AppLanguageService.GetString("Field.TypeIntShort"),
                "decimal" => AppLanguageService.GetString("Field.TypeDecimalShort"),
                "date" => AppLanguageService.GetString("Field.TypeDateShort"),
                "bool" => AppLanguageService.GetString("Field.TypeBoolShort"),
                _ => FieldType
            };
        }
    }

    public bool IsRequired { get; set; }

    public int Sort { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Remark { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? MinLength { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? MaxLength { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? RegexPattern { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? RegexErrorMessage { get; set; }

    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? EnumOptions { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? MinValue { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? MaxValue { get; set; }

    public bool IsDeleted { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string ValidationRuleSummary
    {
        get
        {
            var rules = new List<string>();

            if (IsRequired)
                rules.Add(AppLanguageService.GetString("Field.ValidationRequired"));

            if (MinLength.HasValue || MaxLength.HasValue)
                rules.Add(AppLanguageService.Format("Field.ValidationChars", MinLength?.ToString() ?? "0", MaxLength?.ToString() ?? "∞"));

            if (!string.IsNullOrWhiteSpace(EnumOptions))
                rules.Add(AppLanguageService.GetString("Field.ValidationEnum"));

            if (MinValue.HasValue || MaxValue.HasValue)
                rules.Add($"{MinValue?.ToString() ?? "-∞"}-{MaxValue?.ToString() ?? "∞"}");

            if (!string.IsNullOrWhiteSpace(RegexPattern))
                rules.Add(AppLanguageService.GetString("Field.ValidationRegex"));

            return rules.Count == 0 ? string.Empty : string.Join(AppLanguageService.GetString("Common.ListSeparator"), rules);
        }
    }
}
