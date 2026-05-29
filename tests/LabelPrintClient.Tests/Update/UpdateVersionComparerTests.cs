using LabelPrintClient.Modules.Update.Services;

namespace LabelPrintClient.Tests.Update;

public class UpdateVersionComparerTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.2.0", "1.10.0", false)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    public void IsNewer_UsesVersionOrdering(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateVersionComparer.IsNewer(candidate, current));
    }
}
