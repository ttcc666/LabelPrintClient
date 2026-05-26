using SqlSugar;

namespace LabelPrintClient.Modules.Template.Models;

[SugarTable("label_category")]
[SugarIndex(
    "ix_label_category_enabled_sort",
    nameof(IsEnabled), OrderByType.Asc,
    nameof(Sort), OrderByType.Asc)]
public class LabelCategory
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 0 表示根分类；大于 0 表示父分类 Id。
    /// 不使用 null，避免 SQLite / PostgreSQL 下根分类插入时触发 NOT NULL 约束问题。
    /// </summary>
    public long ParentId { get; set; } = 0;

    public int Sort { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdateTime { get; set; }

    public override string ToString() => Name;
}
