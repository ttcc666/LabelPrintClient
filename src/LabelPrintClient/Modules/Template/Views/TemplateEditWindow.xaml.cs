using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Template.Views;

public partial class TemplateEditWindow : Window
{
    public new LabelTemplate Template { get; private set; }

    public TemplateEditWindow(LabelTemplate? template = null)
    {
        InitializeComponent();
        
        if (template != null)
        {
            // 编辑模式，深拷贝
            Template = new LabelTemplate
            {
                Id = template.Id,
                CategoryId = template.CategoryId,
                Name = template.Name,
                StorageType = template.StorageType,
                TemplatePath = template.TemplatePath,
                TemplateFileName = template.TemplateFileName,
                TemplateContent = template.TemplateContent,
                TemplateHash = template.TemplateHash,
                DataSourceName = template.DataSourceName,
                Version = template.Version,
                IsEnabled = template.IsEnabled,
                CreateTime = template.CreateTime,
                UpdateTime = template.UpdateTime
            };
            TitleText.Text = "编辑模板";
            NameBox.Text = Template.Name;
            IsEnabledBox.IsChecked = Template.IsEnabled;
            
            // 选中存储介质
            if (Template.StorageType == TemplateStorageType.Database)
            {
                StorageTypeBox.SelectedIndex = 1;
            }
            else
            {
                StorageTypeBox.SelectedIndex = 0;
            }
        }
        else
        {
            Template = new LabelTemplate
            {
                IsEnabled = true
            };
            TitleText.Text = "新增模板";
            
            // 根据 App.Settings.RunMode 自动决定默认存储介质
            if (App.Settings.RunMode == AppRunMode.LocalSqlite)
            {
                StorageTypeBox.SelectedIndex = 0; // LocalFile
            }
            else
            {
                StorageTypeBox.SelectedIndex = 1; // Database
            }
        }

        // 根据当前的数据库模式，强制限制下拉框的可选状态
        ApplyStorageTypeLimits();
    }

    private void ApplyStorageTypeLimits()
    {
        if (App.Settings.RunMode == AppRunMode.LocalSqlite)
        {
            // SQLite 模式：只能存本地物理文件，禁用数据库存储
            foreach (var item in StorageTypeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), "Database", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsEnabled = false;
                }
            }
        }
        else if (App.Settings.RunMode == AppRunMode.LanPostgreSql)
        {
            // PgSQL 模式：只能存数据库，禁用本地物理文件
            foreach (var item in StorageTypeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), "LocalFile", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsEnabled = false;
                }
            }
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppMessageBox.Show("请输入模板名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Template.Name = name;
        Template.IsEnabled = IsEnabledBox.IsChecked == true;
        
        // 读取存储介质
        if (StorageTypeBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            Template.StorageType = tag == "Database" 
                ? TemplateStorageType.Database 
                : TemplateStorageType.LocalFile;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}


