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
                ["ProduceDate"] = "2026-05-27",
                ["batch_no"] = "SHOULD_NOT_EXPORT"
            })
        };

        var table = DataTableBuilder.Build([row], fields, "LabelData");

        Assert.True(table.Columns.Contains(TemplateSystemFields.BatchNo));
        Assert.True(table.Columns.Contains("ProductName"));
        Assert.True(table.Columns.Contains("ProduceDate"));
        Assert.False(table.Columns.Contains("_batch_no"));
        Assert.False(table.Columns.Contains("batch_no"));
        Assert.Equal("BATCH-20260526", table.Rows[0][TemplateSystemFields.BatchNo]);
        Assert.Equal("感冒灵颗粒", table.Rows[0]["ProductName"]);
        Assert.Equal("2026-05-27", table.Rows[0]["ProduceDate"]);
    }

    [Fact]
    public void Build_AddsEmptyLegacySystemColumnsWhenTemplateNoLongerHasSystemFields()
    {
        var fields = new List<LabelTemplateField> { Field("ProductName", "产品名称") };
        var row = new LabelImportRow
        {
            RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
            {
                ["ProductName"] = "感冒灵颗粒"
            })
        };

        var table = DataTableBuilder.Build([row], fields, "LabelData");

        Assert.True(table.Columns.Contains("_batch_no"));
        Assert.True(table.Columns.Contains("_serial_no"));
        Assert.Equal(string.Empty, table.Rows[0]["_batch_no"]);
        Assert.Equal(string.Empty, table.Rows[0]["_serial_no"]);
    }

    [Fact]
    public void Build_BatchTemplate_AddsSerialCompatibilityOnly()
    {
        var fields = new List<LabelTemplateField>
        {
            Field(TemplateSystemFields.BatchNo, "批号"),
            Field("ProductName", "产品名称")
        };
        var row = new LabelImportRow
        {
            RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
            {
                [TemplateSystemFields.BatchNo] = "BATCH-001",
                ["ProductName"] = "感冒灵颗粒"
            })
        };

        var table = DataTableBuilder.Build([row], fields, "LabelData");

        Assert.True(table.Columns.Contains(TemplateSystemFields.BatchNo));
        Assert.False(table.Columns.Contains("_batch_no"));
        Assert.True(table.Columns.Contains("_serial_no"));
        Assert.Equal("BATCH-001", table.Rows[0][TemplateSystemFields.BatchNo]);
        Assert.Equal(string.Empty, table.Rows[0]["_serial_no"]);
    }

    [Fact]
    public void Build_SerializedTemplate_AddsBatchCompatibilityOnly()
    {
        var fields = new List<LabelTemplateField>
        {
            Field(TemplateSystemFields.SerialNo, "序列号"),
            Field("ProductName", "产品名称")
        };
        var row = new LabelImportRow
        {
            RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
            {
                [TemplateSystemFields.SerialNo] = "SN-0001",
                ["ProductName"] = "感冒灵颗粒"
            })
        };

        var table = DataTableBuilder.Build([row], fields, "LabelData");

        Assert.True(table.Columns.Contains(TemplateSystemFields.SerialNo));
        Assert.False(table.Columns.Contains("_serial_no"));
        Assert.True(table.Columns.Contains("_batch_no"));
        Assert.Equal("SN-0001", table.Rows[0][TemplateSystemFields.SerialNo]);
        Assert.Equal(string.Empty, table.Rows[0]["_batch_no"]);
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
