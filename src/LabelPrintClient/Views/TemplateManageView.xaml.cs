using System.IO;
using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using LabelPrintClient.Services.Stimulsoft;
using LabelPrintClient.Services.TemplateStorage;

namespace LabelPrintClient.Views;

public partial class TemplateManageView : System.Windows.Controls.UserControl
{
    private const int DefaultTemplatePageSize = 20;

    private CancellationTokenSource? _categoryLoadCts;
    private CancellationTokenSource? _templateLoadCts;
    private CancellationTokenSource? _fieldLoadCts;
    private int _templateCurrentPage = 1;
    private int _templatePageSize = DefaultTemplatePageSize;
    private int _templateTotalRows;
    private int _templateTotalPages = 1;

    public TemplateManageView()
    {
        InitializeComponent();
        RunModeText.Text = App.Settings.RunMode.ToString();
        Loaded += async (_, _) => await RefreshAllAsync();
        Unloaded += (_, _) => CancelPendingLoads();
    }

    private LabelCategory? SelectedCategory => CategoryGrid.SelectedItem as LabelCategory;
    private LabelTemplate? SelectedTemplate => TemplateGrid.SelectedItem as LabelTemplate;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async Task RefreshAllAsync()
    {
        var token = ResetCancellation(ref _categoryLoadCts);
        try
        {
            var categories = await AppDb.Db.Queryable<LabelCategory>()
                .OrderBy(x => x.Sort)
                .ToListAsync();

            if (token.IsCancellationRequested) return;

            CategoryGrid.ItemsSource = categories;
            ClearTemplates();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"刷新失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CategoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _templateCurrentPage = 1;
        await LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync()
    {
        var category = SelectedCategory;
        if (category == null)
        {
            ClearTemplates();
            return;
        }

        var token = ResetCancellation(ref _templateLoadCts);
        try
        {
            var templateQuery = AppDb.Db.Queryable<LabelTemplate>()
                .Where(x => x.CategoryId == category.Id);

            var totalRows = await templateQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)_templatePageSize));
            var currentPage = Math.Clamp(_templateCurrentPage, 1, totalPages);

            var templates = await templateQuery
                .OrderBy(x => x.Name)
                .Skip((currentPage - 1) * _templatePageSize)
                .Take(_templatePageSize)
                .ToListAsync();

            if (token.IsCancellationRequested || SelectedCategory?.Id != category.Id) return;

            _templateTotalRows = totalRows;
            _templateTotalPages = totalPages;
            _templateCurrentPage = currentPage;
            TemplateGrid.ItemsSource = templates;
            FieldGrid.ItemsSource = null;
            UpdateTemplatePagination();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"加载模板失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TemplateGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadFieldsAsync();
    }

    private async Task LoadFieldsAsync()
    {
        var template = SelectedTemplate;
        if (template == null)
        {
            FieldGrid.ItemsSource = null;
            return;
        }

        var token = ResetCancellation(ref _fieldLoadCts);
        try
        {
            var fields = await AppDb.Db.Queryable<LabelTemplateField>()
                .Where(x => x.TemplateId == template.Id)
                .OrderBy(x => x.Sort)
                .ToListAsync();

            if (token.IsCancellationRequested || SelectedTemplate?.Id != template.Id) return;
            FieldGrid.ItemsSource = fields;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"加载字段失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = CategoryNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请输入分类名称。");
            return;
        }

        if (await FindCategoryAsync(name, 0) != null)
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

        await AppDb.Db.Insertable(category).ExecuteCommandAsync();
        CategoryNameBox.Text = string.Empty;
        await RefreshAllAsync();
    }

    private async void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCategory == null)
        {
            System.Windows.MessageBox.Show("请先选择分类。");
            return;
        }

        var categoryId = SelectedCategory.Id;
        var name = TemplateNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请输入模板名称。");
            return;
        }

        if (await FindTemplateAsync(categoryId, name) != null)
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
            CategoryId = categoryId,
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

        await AppDb.Db.Insertable(template).ExecuteCommandAsync();
        TemplateNameBox.Text = string.Empty;
        _templateCurrentPage = 1;
        await LoadTemplatesAsync();
    }

    private async void UploadTemplate_Click(object sender, RoutedEventArgs e)
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

        try
        {
            await RunQueuedAsync(sender, "正在上传模板...", ct => UploadTemplateAsync(template, dialog.FileName, ct));
            await LoadTemplatesAsync();
            System.Windows.MessageBox.Show("模板已保存。");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"模板保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task UploadTemplateAsync(LabelTemplate template, string sourceFileName, CancellationToken cancellationToken)
    {
        if (App.Settings.RunMode == AppRunMode.LocalSqlite)
        {
            var targetPath = BuildLocalTemplatePath(template.Id);
            await Task.Run(() => File.Copy(sourceFileName, targetPath, true), cancellationToken);
            template.StorageType = TemplateStorageType.LocalFile;
            template.TemplatePath = targetPath;
            template.TemplateFileName = Path.GetFileName(sourceFileName);
            template.TemplateHash = await FileHashHelper.GetSha256Async(targetPath, cancellationToken);
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(sourceFileName, cancellationToken);
            template.StorageType = TemplateStorageType.Database;
            template.TemplateFileName = Path.GetFileName(sourceFileName);
            template.TemplateContent = bytes;
            template.TemplatePath = null;
            template.TemplateHash = FileHashHelper.GetSha256(bytes);
        }

        template.UpdateTime = DateTime.Now;
        await AppDb.Db.Updateable(template).ExecuteCommandAsync();
    }

    private async void TemplateFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_templateCurrentPage <= 1) return;
        _templateCurrentPage = 1;
        await LoadTemplatesAsync();
    }

    private async void TemplatePrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_templateCurrentPage <= 1) return;
        _templateCurrentPage--;
        await LoadTemplatesAsync();
    }

    private async void TemplateNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_templateCurrentPage >= _templateTotalPages) return;
        _templateCurrentPage++;
        await LoadTemplatesAsync();
    }

    private async void TemplateLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_templateCurrentPage >= _templateTotalPages) return;
        _templateCurrentPage = _templateTotalPages;
        await LoadTemplatesAsync();
    }

    private async void TemplatePageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _templatePageSize = GetSelectedTemplatePageSize();
        _templateCurrentPage = 1;
        if (SelectedCategory != null)
            await LoadTemplatesAsync();
        else
            UpdateTemplatePagination();
    }

    private async void DesignTemplate_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        if (template == null)
        {
            System.Windows.MessageBox.Show("请先选择模板。");
            return;
        }

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .OrderBy(x => x.Sort)
            .ToListAsync();

        if (fields.Count == 0)
        {
            System.Windows.MessageBox.Show("请先维护模板字段，设计器会根据字段注册 LabelData 数据源。");
            return;
        }

        try
        {
            var storage = LabelTemplateStorageFactory.Create(App.Settings.RunMode);
            var designer = new StiTemplateDesignerService(storage);
            await RunQueuedAsync(sender, "正在打开设计器...", ct => designer.DesignAsync(template, fields, ct));
            await LoadTemplatesAsync();
            System.Windows.MessageBox.Show("模板设计已保存。");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"打开设计器失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddField_Click(object sender, RoutedEventArgs e)
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

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .ToListAsync();
        var duplicateFieldError = GetDuplicateFieldError(fields, name, code);
        if (duplicateFieldError != null)
        {
            System.Windows.MessageBox.Show(duplicateFieldError);
            return;
        }

        var maxSort = fields.Count == 0 ? 0 : fields.Max(x => x.Sort);
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

        await AppDb.Db.Insertable(field).ExecuteCommandAsync();
        FieldNameBox.Text = string.Empty;
        FieldCodeBox.Text = string.Empty;
        FieldRemarkBox.Text = string.Empty;
        await LoadFieldsAsync();
    }

    private async void DeleteField_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGrid.SelectedItem is not LabelTemplateField field) return;
        if (System.Windows.MessageBox.Show($"确定删除字段 {field.FieldName}？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await AppDb.Db.Deleteable<LabelTemplateField>().Where(x => x.Id == field.Id).ExecuteCommandAsync();
        await LoadFieldsAsync();
    }

    private async void SeedDemo_Click(object sender, RoutedEventArgs e)
    {
        var category = await FindCategoryAsync("产品标签", 0);
        if (category == null)
        {
            category = new LabelCategory
            {
                Id = IdHelper.NewId(),
                Name = "产品标签",
                Sort = 10,
                IsEnabled = true
            };
            await AppDb.Db.Insertable(category).ExecuteCommandAsync();
        }

        var template = await FindTemplateAsync(category.Id, "产品基础标签");
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
            await AppDb.Db.Insertable(template).ExecuteCommandAsync();
        }

        var existsFields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .AnyAsync();
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
            await AppDb.Db.Insertable(fields).ExecuteCommandAsync();
        }

        await RefreshAllAsync();
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

    private void ClearTemplates()
    {
        if (TemplateGrid != null)
            TemplateGrid.ItemsSource = null;
        if (FieldGrid != null)
            FieldGrid.ItemsSource = null;

        _templateCurrentPage = 1;
        _templateTotalRows = 0;
        _templateTotalPages = 1;
        UpdateTemplatePagination();
    }

    private void UpdateTemplatePagination()
    {
        if (TemplatePageInfoText == null ||
            TemplateFirstPageButton == null || TemplatePrevPageButton == null ||
            TemplateNextPageButton == null || TemplateLastPageButton == null)
        {
            return;
        }

        TemplatePageInfoText.Text = $"{_templateCurrentPage} / {_templateTotalPages}，共 {_templateTotalRows} 个";

        var hasRows = _templateTotalRows > 0;
        TemplateFirstPageButton.IsEnabled = hasRows && _templateCurrentPage > 1;
        TemplatePrevPageButton.IsEnabled = hasRows && _templateCurrentPage > 1;
        TemplateNextPageButton.IsEnabled = hasRows && _templateCurrentPage < _templateTotalPages;
        TemplateLastPageButton.IsEnabled = hasRows && _templateCurrentPage < _templateTotalPages;
    }

    private int GetSelectedTemplatePageSize()
    {
        if (TemplatePageSizeBox?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var pageSize) &&
            pageSize > 0)
        {
            return pageSize;
        }

        return DefaultTemplatePageSize;
    }

    private async Task<LabelCategory?> FindCategoryAsync(string name, long parentId)
    {
        var categories = await AppDb.Db.Queryable<LabelCategory>()
            .Where(x => x.ParentId == parentId)
            .ToListAsync();
        return categories.FirstOrDefault(x => string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<LabelTemplate?> FindTemplateAsync(long categoryId, string name)
    {
        var templates = await AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.CategoryId == categoryId)
            .ToListAsync();
        return templates.FirstOrDefault(x => string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetDuplicateFieldError(IEnumerable<LabelTemplateField> fields, string name, string code)
    {
        if (fields.Any(x => string.Equals(x.FieldName.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return "字段名已存在，请勿重复新增。";

        if (fields.Any(x => string.Equals(x.FieldCode.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            return "字段编码已存在，请勿重复新增。";

        return null;
    }

    private async Task RunQueuedAsync(object sender, string runningText, Func<CancellationToken, Task> operation)
    {
        var element = sender as UIElement;
        var modeText = App.Settings.RunMode.ToString();

        if (element != null)
            element.IsEnabled = false;
        RunModeText.Text = $"{modeText} · {runningText}";

        try
        {
            await BackgroundTaskQueue.Shared.EnqueueAsync(operation);
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
            RunModeText.Text = modeText;
        }
    }

    private static CancellationToken ResetCancellation(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        return cts.Token;
    }

    private void CancelPendingLoads()
    {
        CancelAndDispose(ref _categoryLoadCts);
        CancelAndDispose(ref _templateLoadCts);
        CancelAndDispose(ref _fieldLoadCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
