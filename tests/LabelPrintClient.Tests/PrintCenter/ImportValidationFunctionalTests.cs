using ClosedXML.Excel;
using LabelPrintClient.Database;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;
using File = System.IO.File;

namespace LabelPrintClient.Tests.PrintCenter;

public class ImportValidationFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task PreviewExcelAsync_WhenRequiredColumnMissing_ReturnsInvalidRow()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Normal);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 1, required: true);
        await TestSeed.FieldAsync(template.Id, "ProductName", "产品名称", 2);
        using var temp = new TempExcelFile();
        CreateWorkbook(temp.Path, ["产品名称"], ["感冒灵颗粒"]);

        var preview = await new LabelImportService().PreviewExcelAsync(template.Id, temp.Path);

        Assert.Single(preview.Rows);
        Assert.False(preview.Rows.Single().IsValid);
        Assert.Contains("缺少必填列：产品编码", preview.Rows.Single().Errors);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task CommitImportAsync_WhenPreviewHasInvalidRows_ThrowsAndDoesNotImport()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Normal);
        var qtyField = await TestSeed.FieldAsync(template.Id, "Qty", "数量", 1, required: true);
        qtyField.FieldType = "int";
        await AppDb.Db.Updateable(qtyField).ExecuteCommandAsync();
        using var temp = new TempExcelFile();
        CreateWorkbook(temp.Path, ["数量 *"], ["abc"]);
        var service = new LabelImportService();
        var preview = await service.PreviewExcelAsync(template.Id, temp.Path);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CommitImportAsync(preview, null, "tester"));

        Assert.Contains("存在错误行", ex.Message);
    }

    private static void CreateWorkbook(string path, IReadOnlyList<string> headers, IReadOnlyList<string> values)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("导入数据");
        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, i + 1).Value = values[i];
        }
        workbook.SaveAs(path);
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
