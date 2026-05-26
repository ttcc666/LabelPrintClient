using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Tests.PrintCenter;

public class DataTableBuilderTests
{
    [Fact]
    public void Build_CreatesColumnsForProvidedFieldsOnly()
    {
        var fields = new List<LabelTemplateField>
        {
            Field("$batch_no", "批号"),
            Field("ProductName", "产品名称")
        };
        var row = new LabelImportRow
        {
            RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
            {
                [TemplateSystemFields.BatchNo] = "BATCH-20260526",
                ["ProductName"] = "感冒灵颗粒",
                ["batch_no"] = "SHOULD_NOT_EXPORT"
            })
        };

        var table = DataTableBuilder.Build([row], fields, "LabelData");

        Assert.True(table.Columns.Contains(TemplateSystemFields.BatchNo));
        Assert.True(table.Columns.Contains("ProductName"));
        Assert.False(table.Columns.Contains("batch_no"));
        Assert.Equal("BATCH-20260526", table.Rows[0][TemplateSystemFields.BatchNo]);
        Assert.Equal("感冒灵颗粒", table.Rows[0]["ProductName"]);
    }

    [Fact]
    public void Build_ExpandsRowsByCopyCount()
    {
        var fields = new List<LabelTemplateField> { Field("ProductName", "产品名称") };
        var row = new LabelImportRow
        {
            RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
            {
                ["ProductName"] = "感冒灵颗粒"
            })
        };

        var table = DataTableBuilder.Build([row], fields, "LabelData", copyCount: 3);

        Assert.Equal(3, table.Rows.Count);
    }

    private static LabelTemplateField Field(string code, string name)
    {
        return new LabelTemplateField
        {
            FieldCode = code,
            FieldName = name,
            FieldType = "string"
        };
    }
}
