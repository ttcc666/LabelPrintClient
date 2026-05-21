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

        if (FindCategory(name, 0) != null)
        {
            System.Windows.MessageBox.Show("分类名称已存在，请勿重复新增。");
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

        if (FindTemplate(SelectedCategory.Id, name) != null)
        {
            System.Windows.MessageBox.Show("当前分类下已存在同名模板，请勿重复新增。");
            return;
        }

        var storageType = App.Settings.RunMode == AppRunMode.LocalSqlite
            ? TemplateStorageType.LocalFile
            : TemplateStorageType.Database;

        var templateId = IdHelper.NewId();
        var template = new LabelTemplate
        {
            Id = templateId,
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
            template.TemplatePath = BuildLocalTemplatePath(template.Id);
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
            var targetPath = BuildLocalTemplatePath(template.Id);
            File.Copy(dialog.FileName, targetPath, true);
            template.StorageType = TemplateStorageType.LocalFile;
            template.TemplatePath = targetPath;
            template.TemplateFileName = Path.GetFileName(dialog.FileName);
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

        var duplicateFieldError = GetDuplicateFieldError(template.Id, name, code);
        if (duplicateFieldError != null)
        {
            System.Windows.MessageBox.Show(duplicateFieldError);
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
        var category = FindCategory("产品标签", 0);
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

        var template = FindTemplate(category.Id, "产品基础标签");
        if (template == null)
        {
            var storageType = App.Settings.RunMode == AppRunMode.LocalSqlite
                ? TemplateStorageType.LocalFile
                : TemplateStorageType.Database;

            var templateId = IdHelper.NewId();
            template = new LabelTemplate
            {
                Id = templateId,
                CategoryId = category.Id,
                Name = "产品基础标签",
                StorageType = storageType,
                DataSourceName = "LabelData",
                TemplateFileName = "产品基础标签.mrt",
                TemplatePath = storageType == TemplateStorageType.LocalFile
                    ? BuildLocalTemplatePath(templateId)
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

    private static string BuildLocalTemplatePath(long templateId)
    {
        return Path.Combine(ResolveTemplateFolder(), $"{templateId}.mrt");
    }

    private static LabelCategory? FindCategory(string name, long parentId)
    {
        return AppDb.Db.Queryable<LabelCategory>()
            .Where(x => x.ParentId == parentId)
            .ToList()
            .FirstOrDefault(x => string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static LabelTemplate? FindTemplate(long categoryId, string name)
    {
        return AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.CategoryId == categoryId)
            .ToList()
            .FirstOrDefault(x => string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetDuplicateFieldError(long templateId, string name, string code)
    {
        var fields = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId)
            .ToList();

        if (fields.Any(x => string.Equals(x.FieldName.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return "字段名已存在，请勿重复新增。";

        if (fields.Any(x => string.Equals(x.FieldCode.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            return "字段编码已存在，请勿重复新增。";

        return null;
    }
}
