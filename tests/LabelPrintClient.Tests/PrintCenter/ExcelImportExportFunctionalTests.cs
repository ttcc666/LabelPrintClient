using ClosedXML.Excel;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;
using File = System.IO.File;
using Path = System.IO.Path;

namespace LabelPrintClient.Tests.PrintCenter;

public class ExcelImportExportFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task ExportAsync_ExcludesBatchAndSerialSystemFields()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.SerialNo, "序列号", 2);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 3, required: true);
        await TestSeed.FieldAsync(template.Id, "ProductName", "产品名称", 4);
        using var temp = new TempExcelFile();

        await new ExcelTemplateExportService().ExportAsync(template.Id, temp.Path);

        using var workbook = new XLWorkbook(temp.Path);
        var dataSheet = workbook.Worksheet("导入数据");
        var instructionSheet = workbook.Worksheet("字段说明");
        var dataHeaders = ReadRowValues(dataSheet, 1, 2);
        var instructionCodes = ReadColumnValues(instructionSheet, 2, 2, 3);

        Assert.Equal(["产品编码 *", "产品名称"], dataHeaders);
        Assert.Equal(["ProductCode", "ProductName"], instructionCodes);
        Assert.DoesNotContain(TemplateSystemFields.BatchNo, instructionCodes);
        Assert.DoesNotContain(TemplateSystemFields.SerialNo, instructionCodes);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task ImportExcelAsync_ForBatchTemplate_DoesNotPersistManualBatchNoOrSystemFields()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Batch);
        await TestSeed.FieldAsync(template.Id, TemplateSystemFields.BatchNo, "批号", 1);
        await TestSeed.FieldAsync(template.Id, "ProductCode", "产品编码", 2, required: true);
        await TestSeed.FieldAsync(template.Id, "ProductName", "产品名称", 3);
        using var temp = new TempExcelFile();
        CreateWorkbook(temp.Path, ["产品编码 *", "产品名称"], ["P001", "感冒灵颗粒"]);

        var batchId = await new LabelImportService().ImportExcelAsync(template.Id, temp.Path, "tester");

        var batch = await AppDb.Db.Queryable<LabelImportBatch>().InSingleAsync(batchId);
        var rows = await AppDb.Db.Queryable<LabelImportRow>()
            .Where(x => x.BatchId == batchId)
            .ToListAsync();
        var data = JsonHelper.Deserialize<Dictionary<string, string>>(rows.Single().RowDataJson)!;

        Assert.Null(batch.BatchNo);
        Assert.Equal(1, batch.ValidRows);
        Assert.Equal("P001", data["ProductCode"]);
        Assert.Equal("感冒灵颗粒", data["ProductName"]);
        Assert.DoesNotContain(TemplateSystemFields.BatchNo, data.Keys);
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

    private static List<string> ReadRowValues(IXLWorksheet sheet, int row, int columnCount)
    {
        return Enumerable.Range(1, columnCount)
            .Select(column => sheet.Cell(row, column).GetString())
            .ToList();
    }

    private static List<string> ReadColumnValues(IXLWorksheet sheet, int column, int startRow, int rowCount)
    {
        return Enumerable.Range(startRow, rowCount)
            .Select(row => sheet.Cell(row, column).GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
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
