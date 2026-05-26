using LabelPrintClient.Database;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;

namespace LabelPrintClient.Tests.PrintCenter;

public class SerialNumberConcurrencyFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task GenerateNextAsync_ForSameRowUnderConcurrency_ProducesUniqueContinuousValues()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Serialized,
            serialPattern: "SN-{seq:0000}");
        template.SerialResetPeriod = SerialResetPeriod.Never;
        await AppDb.Db.Updateable(template).ExecuteCommandAsync();
        var batch = await TestSeed.ImportBatchAsync(template);
        var row = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        });
        var now = new DateTime(2026, 5, 26, 10, 0, 0);
        const int count = 32;

        var tasks = Enumerable.Range(0, count)
            .Select(_ => Task.Run(() => SerialNumberService.GenerateNextAsync(template, row, now)))
            .ToArray();
        var serials = await Task.WhenAll(tasks);

        var ordered = serials.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var expected = Enumerable.Range(1, count)
            .Select(x => $"SN-{x:0000}")
            .ToList();
        var counter = await AppDb.Db.Queryable<LabelSerialCounter>()
            .Where(x => x.TemplateId == template.Id && x.ImportRowId == row.Id && x.CounterKey == "global")
            .SingleAsync();

        Assert.Equal(count, serials.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expected, ordered);
        Assert.Equal(count, counter.CurrentValue);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task GenerateNextAsync_ForDifferentRowsUnderConcurrency_MaintainsIndependentCounters()
    {
        using var database = TestDatabase.Create();
        var category = await TestSeed.CategoryAsync();
        var template = await TestSeed.TemplateAsync(
            category.Id,
            LabelTemplateMode.Serialized,
            serialPattern: "SN-{ProductCode}-{seq:0000}");
        var batch = await TestSeed.ImportBatchAsync(template, totalRows: 2);
        var rowA = await TestSeed.ImportRowAsync(batch, 1, new Dictionary<string, string>
        {
            ["ProductCode"] = "A"
        });
        var rowB = await TestSeed.ImportRowAsync(batch, 2, new Dictionary<string, string>
        {
            ["ProductCode"] = "B"
        });
        var now = new DateTime(2026, 5, 26, 10, 0, 0);

        var tasks = Enumerable.Range(0, 10)
            .SelectMany(_ => new[]
            {
                Task.Run(() => SerialNumberService.GenerateNextAsync(template, rowA, now)),
                Task.Run(() => SerialNumberService.GenerateNextAsync(template, rowB, now))
            })
            .ToArray();
        var serials = await Task.WhenAll(tasks);

        var serialsA = serials.Where(x => x.StartsWith("SN-A-", StringComparison.Ordinal)).OrderBy(x => x).ToList();
        var serialsB = serials.Where(x => x.StartsWith("SN-B-", StringComparison.Ordinal)).OrderBy(x => x).ToList();

        Assert.Equal(Enumerable.Range(1, 10).Select(x => $"SN-A-{x:0000}").ToList(), serialsA);
        Assert.Equal(Enumerable.Range(1, 10).Select(x => $"SN-B-{x:0000}").ToList(), serialsB);
    }
}
