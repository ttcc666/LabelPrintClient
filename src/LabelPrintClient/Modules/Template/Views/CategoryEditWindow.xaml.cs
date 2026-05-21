using System.Windows;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.Template.Views;

public partial class CategoryEditWindow : Window
{
    public LabelCategory Category { get; private set; }

    public CategoryEditWindow(LabelCategory? category = null)
    {
        InitializeComponent();
        if (category != null)
        {
            // 编辑模式，复制一份以防直接修改导致UI显示不一致而未保存
            Category = new LabelCategory
            {
                Id = category.Id,
                Name = category.Name,
                Sort = category.Sort,
                IsEnabled = category.IsEnabled
            };
            TitleText.Text = "编辑分类";
            NameBox.Text = Category.Name;
            IsEnabledBox.IsChecked = Category.IsEnabled;
        }
        else
        {
            Category = new LabelCategory
            {
                IsEnabled = true
            };
            TitleText.Text = "新增分类";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请输入分类名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Category.Name = name;
        Category.IsEnabled = IsEnabledBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}


