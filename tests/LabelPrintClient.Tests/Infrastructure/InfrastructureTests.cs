using System.Text;
using LabelPrintClient.Infrastructure;

namespace LabelPrintClient.Tests.Infrastructure;

public class InfrastructureTests
{
    [Fact]
    public void JsonHelper_RoundTripsDictionary()
    {
        var source = new Dictionary<string, string>
        {
            ["ProductName"] = "感冒灵颗粒",
            ["Qty"] = "10"
        };

        var json = JsonHelper.Serialize(source);
        var result = JsonHelper.Deserialize<Dictionary<string, string>>(json);

        Assert.NotNull(result);
        Assert.Equal("感冒灵颗粒", result["ProductName"]);
        Assert.Equal("10", result["Qty"]);
    }

    [Fact]
    public void FileHashHelper_ReturnsKnownSha256()
    {
        var hash = FileHashHelper.GetSha256(Encoding.UTF8.GetBytes("abc"));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void IdHelper_GeneratesMonotonicUniqueIds()
    {
        var ids = Enumerable.Range(0, 2000)
            .Select(_ => IdHelper.NewId())
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(ids.SequenceEqual(ids.OrderBy(x => x)));
    }
}
