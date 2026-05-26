using LabelPrintClient.Modules.PrintCenter.Services;

namespace LabelPrintClient.Tests.PrintCenter;

public class SerialNumberServiceTests
{
    private static readonly DateTime FixedTime = new(2026, 5, 26, 14, 35, 0);

    [Fact]
    public void IsValidPattern_RequiresSequenceForSerializedMode()
    {
        var isValid = SerialNumberService.IsValidPattern("SN-{yyyy}{MM}{dd}", ["ProductCode"], out var error);

        Assert.False(isValid);
        Assert.Contains("{seq}", error);
    }

    [Fact]
    public void IsValidBatchPattern_RejectsSequenceVariable()
    {
        var isValid = SerialNumberService.IsValidBatchPattern("BATCH-{yyyy}{seq:0000}", ["ProductCode"], out var error);

        Assert.False(isValid);
        Assert.Contains("不支持", error);
    }

    [Fact]
    public void PreviewBatch_FormatsDateAndRowVariablesWithoutSequence()
    {
        var row = new Dictionary<string, string>
        {
            ["ProductCode"] = "P001"
        };

        var value = SerialNumberService.PreviewBatch("BATCH-{ProductCode}-{yyyy}{MM}{dd}", FixedTime, row);

        Assert.Equal("BATCH-P001-20260526", value);
    }

    [Fact]
    public void Preview_FormatsSerializedSequenceWithPadding()
    {
        var value = SerialNumberService.Preview("SN-{yyyy}{MM}{dd}-{seq:0000}", FixedTime, 12);

        Assert.Equal("SN-20260526-0012", value);
    }
}
