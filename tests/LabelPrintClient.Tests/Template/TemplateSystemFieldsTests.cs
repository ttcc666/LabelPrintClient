using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Tests.Template;

public class TemplateSystemFieldsTests
{
    [Theory]
    [InlineData("$batch_no")]
    [InlineData("$BATCH_NO")]
    [InlineData("$serial_no")]
    public void IsSystemField_RecognizesOnlyDollarPrefixedFields(string fieldCode)
    {
        Assert.True(TemplateSystemFields.IsSystemField(fieldCode));
        Assert.True(TemplateSystemFields.IsManagedSystemField(fieldCode));
    }

    [Theory]
    [InlineData("batch_no")]
    [InlineData("serial_no")]
    [InlineData("ProductName")]
    public void IsSystemField_DoesNotTreatLegacyOrBusinessFieldsAsSystem(string fieldCode)
    {
        Assert.False(TemplateSystemFields.IsSystemField(fieldCode));
        Assert.False(TemplateSystemFields.IsManagedSystemField(fieldCode));
    }

    [Fact]
    public void LabelTemplate_TemplateModeText_ReturnsChineseDisplayName()
    {
        Assert.Equal("普通", new LabelTemplate { TemplateMode = LabelTemplateMode.Normal }.TemplateModeText);
        Assert.Equal("批次", new LabelTemplate { TemplateMode = LabelTemplateMode.Batch }.TemplateModeText);
        Assert.Equal("序列化", new LabelTemplate { TemplateMode = LabelTemplateMode.Serialized }.TemplateModeText);
    }
}
