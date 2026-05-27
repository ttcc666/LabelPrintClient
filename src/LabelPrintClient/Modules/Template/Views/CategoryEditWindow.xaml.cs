using System.Windows;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;

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
            TitleText.Text = AppLanguageService.GetString("Category.EditActionTitle");
            NameBox.Text = Category.Name;
            IsEnabledBox.IsChecked = Category.IsEnabled;
        }
        else
        {
            Category = new LabelCategory
            {
                IsEnabled = true
            };
            TitleText.Text = AppLanguageService.GetString("Category.AddTitle");
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppMessageBox.Show(AppLanguageService.GetString("Category.NameRequired"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
