using System.IO;
using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using LabelPrintClient.Services.Stimulsoft;
using LabelPrintClient.Services.TemplateStorage;
using Microsoft.Win32;

namespace LabelPrintClient.Views;

public partial class TemplateManageView : System.Windows.Controls.UserControl
{
    public TemplateManageView()
    {
        InitializeComponent();
        RunModeText.Text = App.Settings.RunMode.ToString();
        Loaded += (_, _) => RefreshAll();
    }

    private LabelCategory? SelectedCategory => CategoryGrid.SelectedItem as LabelCategory;
    private LabelTemplate? SelectedTemplate => TemplateGrid.SelectedItem as LabelTemplate;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        CategoryGrid.ItemsSource = AppDb.Db.Queryable<LabelCategory>().OrderBy(x => x.Sort).ToList();
        TemplateGrid.ItemsSource = null;
        FieldGrid.ItemsSource = null;
    }

    private void CategoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedCategory == null) return;
        TemplateGrid.ItemsSource = AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.CategoryId == SelectedCategory.Id)
            .OrderBy(x => x.Name)
            .ToList();
        FieldGrid.ItemsSource = null;
    }

    private void TemplateGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadFields();
    }

    private void LoadFields()
    {
        if (SelectedTemplate == null)
        {
            FieldGrid.ItemsSource = null;
            return;
        }

        FieldGrid.ItemsSource = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == SelectedTemplate.Id)
            .OrderBy(x => x.Sort)
            .ToList();
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = CategoryNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请输入分类名称。");
            return;
        }

        var category = new LabelCategory
        {
            Id = IdHelper.NewId(),
            Name = name,
            Sort = 100,
            IsEnabled = true
        };

        AppDb.Db.Insertable(category).ExecuteCommand();
        CategoryNameBox.Text = string.Empty;
        RefreshAll();
    }

    private void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCategory == null)
        {
            System.Windows.MessageBox.Show("请先选择分类。");
            return;
        }

        var name = TemplateNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请输入模板名称。");
            return;
        }

        var storageType = App.Settings.RunMode == AppRunMode.LocalSqlite
            ? TemplateStorageType.LocalFile
            : TemplateStorageType.Database;

        var template = new LabelTemplate
        {
            Id = IdHelper.NewId(),
            CategoryId = SelectedCategory.Id,
            Name = name,
            StorageType = storageType,
            DataSourceName = "LabelData",
            TemplateFileName = $"{name}.mrt",
            Version = 1,
            IsEnabled = true
        };

        if (storageType == TemplateStorageType.LocalFile)
        {
            var folder = ResolveTemplateFolder();
            Directory.CreateDirectory(folder);
            template.TemplatePath = Path.Combine(folder, $"{name}.mrt");
        }

        AppDb.Db.Insertable(template).ExecuteCommand();
        TemplateNameBox.Text = string.Empty;
        CategoryGrid_SelectionChanged(sender, null!);
    }

    private void UploadTemplate_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        if (template == null)
        {
            System.Windows.MessageBox.Show("请先选择模板。");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "STI 报表模板|*.mrt|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        if (App.Settings.RunMode == AppRunMode.LocalSqlite)
        {
            var folder = ResolveTemplateFolder();
            Directory.CreateDirectory(folder);
            var targetPath = Path.Combine(folder, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, targetPath, true);
            template.StorageType = TemplateStorageType.LocalFile;
            template.TemplatePath = targetPath;
            template.TemplateFileName = Path.GetFileName(targetPath);
            template.TemplateHash = FileHashHelper.GetSha256(targetPath);
        }
        else
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            template.StorageType = TemplateStorageType.Database;
            template.TemplateFileName = Path.GetFileName(dialog.FileName);
            template.TemplateContent = bytes;
            template.TemplatePath = null;
            template.TemplateHash = FileHashHelper.GetSha256(bytes);
        }

        template.UpdateTime = DateTime.Now;
        AppDb.Db.Updateable(template).ExecuteCommand();
        CategoryGrid_SelectionChanged(sender, null!);
        System.Windows.MessageBox.Show("模板已保存。");
    }

    private void DesignTemplate_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        if (template == null)
        {
            System.Windows.MessageBox.Show("请先选择模板。");
            return;
        }

        var fields = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .OrderBy(x => x.Sort)
            .ToList();

        if (fields.Count == 0)
        {
            System.Windows.MessageBox.Show("请先维护模板字段，设计器会根据字段注册 LabelData 数据源。");
            return;
        }

        var storage = LabelTemplateStorageFactory.Create(App.Settings.RunMode);
        var designer = new StiTemplateDesignerService(storage);
        designer.Design(template, fields);
        System.Windows.MessageBox.Show("模板设计已保存。");
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        if (template == null)
        {
            System.Windows.MessageBox.Show("请先选择模板。");
            return;
        }

        var name = FieldNameBox.Text.Trim();
        var code = FieldCodeBox.Text.Trim();
        var type = ((ComboBoxItem)FieldTypeBox.SelectedItem).Content?.ToString() ?? "string";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            System.Windows.MessageBox.Show("字段名和字段编码不能为空。");
            return;
        }

        var maxSort = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .Max(x => x.Sort);

        var field = new LabelTemplateField
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            FieldName = name,
            FieldCode = code,
            FieldType = type,
            IsRequired = FieldRequiredBox.IsChecked == true,
            Remark = FieldRemarkBox.Text.Trim(),
            Sort = maxSort + 10
        };

        AppDb.Db.Insertable(field).ExecuteCommand();
        FieldNameBox.Text = string.Empty;
        FieldCodeBox.Text = string.Empty;
        FieldRemarkBox.Text = string.Empty;
        LoadFields();
    }

    private void DeleteField_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGrid.SelectedItem is not LabelTemplateField field) return;
        if (System.Windows.MessageBox.Show($"确定删除字段 {field.FieldName}？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        AppDb.Db.Deleteable<LabelTemplateField>().Where(x => x.Id == field.Id).ExecuteCommand();
        LoadFields();
    }

    private void SeedDemo_Click(object sender, RoutedEventArgs e)
    {
        var category = AppDb.Db.Queryable<LabelCategory>().First(x => x.Name == "产品标签");
        if (category == null)
        {
            category = new LabelCategory
            {
                Id = IdHelper.NewId(),
                Name = "产品标签",
                Sort = 10,
                IsEnabled = true
            };
            AppDb.Db.Insertable(category).ExecuteCommand();
        }

        var template = AppDb.Db.Queryable<LabelTemplate>().First(x => x.Name == "产品基础标签");
        if (template == null)
        {
            var storageType = App.Settings.RunMode == AppRunMode.LocalSqlite
                ? TemplateStorageType.LocalFile
                : TemplateStorageType.Database;

            template = new LabelTemplate
            {
                Id = IdHelper.NewId(),
                CategoryId = category.Id,
                Name = "产品基础标签",
                StorageType = storageType,
                DataSourceName = "LabelData",
                TemplateFileName = "产品基础标签.mrt",
                TemplatePath = storageType == TemplateStorageType.LocalFile
                    ? Path.Combine(ResolveTemplateFolder(), "产品基础标签.mrt")
                    : null,
                Version = 1,
                IsEnabled = true
            };
            AppDb.Db.Insertable(template).ExecuteCommand();
        }

        var existsFields = AppDb.Db.Queryable<LabelTemplateField>().Where(x => x.TemplateId == template.Id).Any();
        if (!existsFields)
        {
            var fields = new List<LabelTemplateField>
            {
                NewField(template.Id, "产品名称", "ProductName", "string", true, 10, "产品中文名称"),
                NewField(template.Id, "条码", "Barcode", "string", true, 20, "一维码或二维码内容"),
                NewField(template.Id, "规格", "Spec", "string", false, 30, "如 500ml"),
                NewField(template.Id, "数量", "Qty", "int", true, 40, "打印数量或产品数量"),
                NewField(template.Id, "生产日期", "ProduceDate", "date", false, 50, "yyyy-MM-dd")
            };
            AppDb.Db.Insertable(fields).ExecuteCommand();
        }

        RefreshAll();
        System.Windows.MessageBox.Show("示例分类、模板和字段已初始化。请继续上传或设计 .mrt 模板。");
    }

    private static LabelTemplateField NewField(long templateId, string name, string code, string type, bool required, int sort, string remark)
    {
        return new LabelTemplateField
        {
            Id = IdHelper.NewId(),
            TemplateId = templateId,
            FieldName = name,
            FieldCode = code,
            FieldType = type,
            IsRequired = required,
            Sort = sort,
            Remark = remark
        };
    }

    private static string ResolveTemplateFolder()
    {
        var folder = App.Settings.LocalTemplateFolder;
        if (!Path.IsPathRooted(folder))
            folder = Path.Combine(AppContext.BaseDirectory, folder);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
