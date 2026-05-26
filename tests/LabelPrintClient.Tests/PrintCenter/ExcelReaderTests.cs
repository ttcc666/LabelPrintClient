using ClosedXML.Excel;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using File = System.IO.File;

namespace LabelPrintClient.Tests.PrintCenter;

public class ExcelReaderTests
{
    [Fact]
    public void ReadRows_ValidatesRequiredTypesAndRules()
    {
        using var temp = new TempExcelFile();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("导入数据");
            sheet.Cell(1, 1).Value = "产品名称 *";
            sheet.Cell(1, 2).Value = "数量 *";
            sheet.Cell(1, 3).Value = "状态";
            sheet.Cell(2, 1).Value = "感冒灵颗粒";
            sheet.Cell(2, 2).Value = "12";
            sheet.Cell(2, 3).Value = "启用";
            sheet.Cell(3, 1).Value = "";
            sheet.Cell(3, 2).Value = "abc";
            sheet.Cell(3, 3).Value = "未知";
            workbook.SaveAs(temp.Path);
        }

        var fields = new List<LabelTemplateField>
        {
            Field("ProductName", "产品名称", "string", required: true),
            Field("Qty", "数量", "int", required: true, minValue: 1, maxValue: 99),
            Field("Status", "状态", "string", enumOptions: "启用\n停用")
        };

        var rows = ExcelReader.ReadRows(temp.Path, fields);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsValid);
        Assert.Equal("12", rows[0].Data["Qty"]);
        Assert.False(rows[1].IsValid);
        Assert.Contains("字段【产品名称】不能为空", rows[1].Errors);
        Assert.Contains("字段【数量】必须是整数", rows[1].Errors);
        Assert.Contains("字段【状态】不在允许值范围内", rows[1].Errors);
    }

    private static LabelTemplateField Field(
        string code,
        string name,
        string type,
        bool required = false,
        decimal? minValue = null,
        decimal? maxValue = null,
        string? enumOptions = null)
    {
        return new LabelTemplateField
        {
            FieldCode = code,
            FieldName = name,
            FieldType = type,
            IsRequired = required,
            MinValue = minValue,
            MaxValue = maxValue,
            EnumOptions = enumOptions
        };
    }

    private sealed class TempExcelFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
