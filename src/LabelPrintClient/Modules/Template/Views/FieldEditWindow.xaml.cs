using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Template.Views;

public partial class FieldEditWindow : Window
{
    public LabelTemplateField Field { get; private set; }

    public FieldEditWindow(LabelTemplateField? field = null, string? title = null)
    {
        InitializeComponent();

        if (field != null)
        {
            // 编辑模式，深拷贝
            Field = new LabelTemplateField
            {
                Id = field.Id,
                TemplateId = field.TemplateId,
                FieldName = field.FieldName,
                FieldCode = field.FieldCode,
                FieldType = field.FieldType,
                IsRequired = field.IsRequired,
                Remark = field.Remark,
                Sort = field.Sort,
                MinLength = field.MinLength,
                MaxLength = field.MaxLength,
                RegexPattern = field.RegexPattern,
                RegexErrorMessage = field.RegexErrorMessage,
                EnumOptions = field.EnumOptions,
                MinValue = field.MinValue,
                MaxValue = field.MaxValue
            };
            TitleText.Text = title ?? "编辑字段";
            FieldNameBox.Text = Field.FieldName;
            FieldCodeBox.Text = Field.FieldCode;
            IsRequiredBox.IsChecked = Field.IsRequired;
            RemarkBox.Text = Field.Remark ?? string.Empty;
            MinLengthBox.Text = Field.MinLength?.ToString() ?? string.Empty;
            MaxLengthBox.Text = Field.MaxLength?.ToString() ?? string.Empty;
            RegexPatternBox.Text = Field.RegexPattern ?? string.Empty;
            RegexErrorMessageBox.Text = Field.RegexErrorMessage ?? string.Empty;
            EnumOptionsBox.Text = Field.EnumOptions ?? string.Empty;
            MinValueBox.Text = Field.MinValue?.ToString() ?? string.Empty;
            MaxValueBox.Text = Field.MaxValue?.ToString() ?? string.Empty;
            SelectFieldType(Field.FieldType);
        }
        else
        {
            Field = new LabelTemplateField
            {
                FieldType = "string",
                IsRequired = false
            };
            TitleText.Text = title ?? "新增字段";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = FieldNameBox.Text.Trim();
        var code = FieldCodeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            AppMessageBox.Show("请输入字段名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            AppMessageBox.Show("请输入字段编码。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadNullableInt(MinLengthBox.Text, "最小长度", out var minLength) ||
            !TryReadNullableInt(MaxLengthBox.Text, "最大长度", out var maxLength) ||
            !TryReadNullableDecimal(MinValueBox.Text, "最小数值", out var minValue) ||
            !TryReadNullableDecimal(MaxValueBox.Text, "最大数值", out var maxValue))
        {
            return;
        }

        if (minLength.HasValue && maxLength.HasValue && minLength.Value > maxLength.Value)
        {
            AppMessageBox.Show("最小长度不能大于最大长度。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (minValue.HasValue && maxValue.HasValue && minValue.Value > maxValue.Value)
        {
            AppMessageBox.Show("最小数值不能大于最大数值。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var regexPattern = NullIfWhiteSpace(RegexPatternBox.Text);
        if (!string.IsNullOrWhiteSpace(regexPattern))
        {
            try
            {
                _ = new Regex(regexPattern, RegexOptions.None, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                AppMessageBox.Show($"正则表达式无效：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        Field.FieldName = name;
        Field.FieldCode = code;
        Field.IsRequired = IsRequiredBox.IsChecked == true;
        Field.Remark = NullIfWhiteSpace(RemarkBox.Text);
        Field.MinLength = minLength;
        Field.MaxLength = maxLength;
        Field.RegexPattern = regexPattern;
        Field.RegexErrorMessage = NullIfWhiteSpace(RegexErrorMessageBox.Text);
        Field.EnumOptions = NullIfWhiteSpace(EnumOptionsBox.Text);
        Field.MinValue = minValue;
        Field.MaxValue = maxValue;

        if (FieldTypeBox.SelectedItem is ComboBoxItem item)
        {
            Field.FieldType = item.Tag?.ToString() ?? "string";
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SelectFieldType(string fieldType)
    {
        foreach (var item in FieldTypeBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), fieldType, StringComparison.OrdinalIgnoreCase))
            {
                FieldTypeBox.SelectedItem = item;
                return;
            }
        }
    }

    private static bool TryReadNullableInt(string text, string displayName, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!int.TryParse(text.Trim(), out var parsed) || parsed < 0)
        {
            AppMessageBox.Show($"{displayName}必须是大于等于 0 的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadNullableDecimal(string text, string displayName, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!decimal.TryParse(text.Trim(), out var parsed))
        {
            AppMessageBox.Show($"{displayName}必须是数字。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        value = parsed;
        return true;
    }

    private static string? NullIfWhiteSpace(string text)
    {
        var value = text.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
