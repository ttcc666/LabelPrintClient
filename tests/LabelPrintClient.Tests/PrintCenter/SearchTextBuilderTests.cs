using LabelPrintClient.Modules.PrintCenter.Services;

namespace LabelPrintClient.Tests.PrintCenter;

public class SearchTextBuilderTests
{
    [Fact]
    public void FromDictionary_IncludesKeysAndValuesAndNormalizesWhitespace()
    {
        var text = SearchTextBuilder.FromDictionary(new Dictionary<string, string>
        {
            ["ProductName"] = " 感冒灵颗粒 ",
            ["Barcode"] = "6901234567890"
        });

        Assert.Equal("ProductName 感冒灵颗粒 Barcode 6901234567890", text);
    }

    [Fact]
    public void FromJson_FallsBackToNormalizedRawTextWhenJsonInvalid()
    {
        var text = SearchTextBuilder.FromJson("  invalid\r\n json\tvalue  ");

        Assert.Equal("invalid json value", text);
    }
}
