using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.Template.Views;

public partial class FieldEditWindow : Window
{
    public LabelTemplateField Field { get; private set; }

    public FieldEditWindow(LabelTemplateField? field = null)
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
                Sort = field.Sort
            };
            TitleText.Text = "编辑字段";
            FieldNameBox.Text = Field.FieldName;
            FieldCodeBox.Text = Field.FieldCode;
            IsRequiredBox.IsChecked = Field.IsRequired;
            RemarkBox.Text = Field.Remark ?? string.Empty;
            SelectFieldType(Field.FieldType);
        }
        else
        {
            Field = new LabelTemplateField
            {
                FieldType = "string",
                IsRequired = false
            };
            TitleText.Text = "新增字段";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = FieldNameBox.Text.Trim();
        var code = FieldCodeBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请输入字段名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            System.Windows.MessageBox.Show("请输入字段编码。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Field.FieldName = name;
        Field.FieldCode = code;
        Field.IsRequired = IsRequiredBox.IsChecked == true;
        Field.Remark = RemarkBox.Text.Trim();
        
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
}
