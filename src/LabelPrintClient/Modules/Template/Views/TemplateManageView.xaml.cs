using System.IO;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Auth.Services;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.Template.Services;
using LabelPrintClient.Services;
using SqlSugar;
using Stimulsoft.Report;

namespace LabelPrintClient.Modules.Template.Views;

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
        RunModeText.Text = GetRunModeText();
        Loaded += async (_, _) => await RefreshAllAsync();
        Loaded += (_, _) => AppLanguageService.LanguageChanged += AppLanguageService_LanguageChanged;
        Unloaded += (_, _) =>
        {
            CancelPendingLoads();
            AppLanguageService.LanguageChanged -= AppLanguageService_LanguageChanged;
        };
    }

    private LabelCategory? SelectedCategory => CategoryGrid.SelectedItem as LabelCategory;
    private LabelTemplate? SelectedTemplate => TemplateGrid.SelectedItem as LabelTemplate;
    private LabelTemplateField? SelectedField => FieldGrid.SelectedItem as LabelTemplateField;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async void CategoryFilter_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async void ClearCategoryFilter_Click(object sender, RoutedEventArgs e)
    {
        CategorySearchBox.Text = string.Empty;
        await RefreshAllAsync();
    }

    private async void CategorySearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        await RefreshAllAsync();
    }

    private async void TemplateFilter_Click(object sender, RoutedEventArgs e)
    {
        _templateCurrentPage = 1;
        await LoadTemplatesAsync();
    }

    private async void ClearTemplateFilter_Click(object sender, RoutedEventArgs e)
    {
        TemplateSearchBox.Text = string.Empty;
        _templateCurrentPage = 1;
        await LoadTemplatesAsync();
    }

    private async void TemplateSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        _templateCurrentPage = 1;
        await LoadTemplatesAsync();
    }

    private async void FieldFilter_Click(object sender, RoutedEventArgs e) => await LoadFieldsAsync();

    private async void ClearFieldFilter_Click(object sender, RoutedEventArgs e)
    {
        FieldSearchBox.Text = string.Empty;
        await LoadFieldsAsync();
    }

    private async void FieldSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        await LoadFieldsAsync();
    }

    private async Task RefreshAllAsync()
    {
        var token = ResetCancellation(ref _categoryLoadCts);
        try
        {
            var keyword = CategorySearchBox?.Text.Trim() ?? string.Empty;
            var categoryQuery = AppDb.Db.Queryable<LabelCategory>();
            if (!string.IsNullOrWhiteSpace(keyword))
                categoryQuery = categoryQuery.Where(x => x.Name.Contains(keyword));

            var categories = await categoryQuery
                    .OrderBy(x => x.Sort)
                    .ToListAsync();

            if (token.IsCancellationRequested ||
                (CategorySearchBox?.Text.Trim() ?? string.Empty) != keyword)
            {
                return;
            }

            CategoryGrid.ItemsSource = categories;
            ClearTemplates();
            UpdateEmptyStates();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(AppLanguageService.Format("Template.RefreshFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            var keyword = TemplateSearchBox?.Text.Trim() ?? string.Empty;
            var templateQuery = AppDb.Db.Queryable<LabelTemplate>()
                .Where(x => x.CategoryId == category.Id);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                if (TryParseTemplateStorageType(keyword, out var storageType))
                    templateQuery = templateQuery.Where(x => x.StorageType == storageType || x.Name.Contains(keyword) || (x.TemplateFileName != null && x.TemplateFileName.Contains(keyword)));
                else
                    templateQuery = templateQuery.Where(x => x.Name.Contains(keyword) || (x.TemplateFileName != null && x.TemplateFileName.Contains(keyword)));
            }

            RefAsync<int> totalRowsRef = 0;
            var currentPage = Math.Max(1, _templateCurrentPage);
            var templates = await templateQuery
                .OrderBy(x => x.Name)
                .ToPageListAsync(currentPage, _templatePageSize, totalRowsRef);
            var totalRows = totalRowsRef.Value;
            var totalPages = Math.Max(1, (totalRows + _templatePageSize - 1) / _templatePageSize);

            if (currentPage > totalPages)
            {
                currentPage = totalPages;
                totalRowsRef = 0;
                templates = await templateQuery
                    .OrderBy(x => x.Name)
                    .ToPageListAsync(currentPage, _templatePageSize, totalRowsRef);
                totalRows = totalRowsRef.Value;
                totalPages = Math.Max(1, (totalRows + _templatePageSize - 1) / _templatePageSize);
            }

            if (token.IsCancellationRequested ||
                SelectedCategory?.Id != category.Id ||
                (TemplateSearchBox?.Text.Trim() ?? string.Empty) != keyword)
            {
                return;
            }

            _templateTotalRows = totalRows;
            _templateTotalPages = totalPages;
            _templateCurrentPage = currentPage;
            TemplateGrid.ItemsSource = templates;
            FieldGrid.ItemsSource = null;
            UpdateTemplatePagination();
            UpdateEmptyStates();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(AppLanguageService.Format("Template.LoadTemplatesFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TemplateGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadFieldsAsync();
    }

    private async Task LoadFieldsAsync(long? selectFieldId = null)
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
            var keyword = FieldSearchBox?.Text.Trim() ?? string.Empty;
            var fieldQuery = AppDb.Db.Queryable<LabelTemplateField>()
                .Where(x => x.TemplateId == template.Id && !x.IsDeleted);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                fieldQuery = fieldQuery.Where(x =>
                    x.FieldName.Contains(keyword) ||
                    x.FieldCode.Contains(keyword) ||
                    x.FieldType.Contains(keyword) ||
                    (x.RegexPattern != null && x.RegexPattern.Contains(keyword)) ||
                    (x.EnumOptions != null && x.EnumOptions.Contains(keyword)) ||
                    (x.Remark != null && x.Remark.Contains(keyword)));
            }

            var fields = await fieldQuery
                    .OrderBy(x => x.Sort)
                    .ToListAsync();

            if (token.IsCancellationRequested ||
                SelectedTemplate?.Id != template.Id ||
                (FieldSearchBox?.Text.Trim() ?? string.Empty) != keyword)
            {
                return;
            }
            FieldGrid.ItemsSource = fields;
            if (selectFieldId.HasValue)
                FieldGrid.SelectedItem = fields.FirstOrDefault(x => x.Id == selectFieldId.Value);
            UpdateEmptyStates();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(AppLanguageService.Format("Template.LoadFieldsFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region 分类维护 (Dialog 方式)

    private async void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateCategoryCreate, "新增分类")) return;

        var parentWindow = Window.GetWindow(this);
        var win = new CategoryEditWindow
        {
            Owner = parentWindow
        };

        if (win.ShowDialog() != true) return;

        var category = win.Category;
        if (await FindCategoryAsync(category.Name, 0) != null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.CategoryNameExists"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        category.Id = IdHelper.NewId();
        category.Sort = 100;

        await AppDb.Db.Insertable(category).ExecuteCommandAsync();
        await RefreshAllAsync();
    }

    private async void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateCategoryEdit, "编辑分类")) return;

        var category = SelectedCategory;
        if (category == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectCategoryToEdit"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parentWindow = Window.GetWindow(this);
        var win = new CategoryEditWindow(category)
        {
            Owner = parentWindow
        };

        if (win.ShowDialog() != true) return;

        var edited = win.Category;
        var existing = await FindCategoryAsync(edited.Name, 0);
        if (existing != null && existing.Id != edited.Id)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.CategoryRenameExists"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await AppDb.Db.Updateable(edited).ExecuteCommandAsync();
        await RefreshAllAsync();
    }

    private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateCategoryDelete, "删除分类")) return;

        var category = SelectedCategory;
        if (category == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectCategoryToDelete"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (AppMessageBox.Show(AppLanguageService.Format("Template.DeleteCategoryConfirm", category.Name), AppLanguageService.GetString("Template.DeleteCategoryTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await AppDb.UseTranAsync(async () =>
        {
            var templates = await AppDb.Db.Queryable<LabelTemplate>().Where(x => x.CategoryId == category.Id).ToListAsync();
            var templateIds = templates.Select(x => x.Id).ToList();

            if (templateIds.Count > 0)
            {
                // 删除关联字段
                await AppDb.Db.Deleteable<LabelTemplateField>().Where(x => templateIds.Contains(x.TemplateId)).ExecuteCommandAsync();
                // 删除关联模板
                await AppDb.Db.Deleteable<LabelTemplate>().Where(x => templateIds.Contains(x.Id)).ExecuteCommandAsync();
            }

            // 删除分类本身
            await AppDb.Db.Deleteable<LabelCategory>().Where(x => x.Id == category.Id).ExecuteCommandAsync();
        });

        await RefreshAllAsync();
    }

    #endregion 分类维护 (Dialog 方式)

    #region 模板维护 (Dialog 方式)

    private async void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateCreate, "新增模板")) return;

        var category = SelectedCategory;
        if (category == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectCategoryBeforeAddTemplate"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parentWindow = Window.GetWindow(this);
        var win = new TemplateEditWindow
        {
            Owner = parentWindow
        };

        if (win.ShowDialog() != true) return;

        var template = win.Template;
        if (await FindTemplateAsync(category.Id, template.Name) != null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.TemplateNameExists"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        template.Id = IdHelper.NewId();
        template.CategoryId = category.Id;
        template.DataSourceName = "LabelData";
        template.TemplateFileName = $"{template.Name}.mrt";
        template.Version = 1;

        if (template.StorageType == TemplateStorageType.LocalFile)
        {
            template.TemplatePath = BuildLocalTemplatePath(template.Id);
        }

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Insertable(template).ExecuteCommandAsync();

            await TemplateSystemFieldService.EnsureModeFieldsAsync(template);
        });

        _templateCurrentPage = 1;
        await LoadTemplatesAsync();
    }

    private async void EditTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateEdit, "编辑模板")) return;

        var template = SelectedTemplate;
        if (template == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectTemplateToEdit"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 查询该模板下已维护的字段编码以供序列号规则强校验
        var allowedFields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && !x.IsDeleted)
            .Select(x => x.FieldCode)
            .ToListAsync();

        var parentWindow = Window.GetWindow(this);
        var win = new TemplateEditWindow(template, allowedFields)
        {
            Owner = parentWindow
        };

        if (win.ShowDialog() != true) return;

        var edited = win.Template;
        var existing = await FindTemplateAsync(edited.CategoryId, edited.Name);
        if (existing != null && existing.Id != edited.Id)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.TemplateRenameExists"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 处理存储介质转换时的路径/内容调整
        if (edited.StorageType == TemplateStorageType.LocalFile && template.StorageType == TemplateStorageType.Database)
        {
            edited.TemplatePath = BuildLocalTemplatePath(edited.Id);
            edited.TemplateContent = null;
        }
        else if (edited.StorageType == TemplateStorageType.Database && template.StorageType == TemplateStorageType.LocalFile)
        {
            edited.TemplatePath = null;
        }

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Updateable(edited).ExecuteCommandAsync();

            if (win.ResetSerialCounter)
            {
                var now = DateTime.Now;
                var counterKey = edited.SerialResetPeriod switch
                {
                    SerialResetPeriod.Daily => now.ToString("yyyyMMdd"),
                    SerialResetPeriod.Monthly => now.ToString("yyyyMM"),
                    SerialResetPeriod.Yearly => now.ToString("yyyy"),
                    _ => "global"
                };

                await AppDb.Db.Deleteable<LabelSerialCounter>()
                    .Where(x => x.TemplateId == edited.Id && x.CounterKey == counterKey)
                    .ExecuteCommandAsync();
            }

            if (win.ClearLockedBatchNumbers)
            {
                await TemplateBatchBindingService.ClearLockedBatchNumbersAsync(edited.Id);
            }

            await TemplateSystemFieldService.EnsureModeFieldsAsync(edited);
        });

        if (win.TemplateModeChanged)
        {
            AppMessageBox.Show(
                AppLanguageService.GetString("Template.ModeChangedReminder"),
                AppLanguageService.GetString("Template.ModeChangedReminderTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        await LoadTemplatesAsync();
    }

    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateDelete, "删除模板")) return;

        var template = SelectedTemplate;
        if (template == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectTemplateToDelete"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (AppMessageBox.Show(AppLanguageService.Format("Template.DeleteTemplateConfirm", template.Name), AppLanguageService.GetString("Template.DeleteTemplateTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Deleteable<LabelTemplateField>().Where(x => x.TemplateId == template.Id).ExecuteCommandAsync();
            await AppDb.Db.Deleteable<LabelTemplate>().Where(x => x.Id == template.Id).ExecuteCommandAsync();
        });

        // 本地物理文件删除
        if (template.StorageType == TemplateStorageType.LocalFile && !string.IsNullOrWhiteSpace(template.TemplatePath))
        {
            try
            {
                if (File.Exists(template.TemplatePath))
                    File.Delete(template.TemplatePath);
            }
            catch { }
        }

        await LoadTemplatesAsync();
    }

    #endregion 模板维护 (Dialog 方式)

    #region 字段维护 (Dialog 方式)

    private async void AddField_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateFieldCreate, "新增字段")) return;

        var template = SelectedTemplate;
        if (template == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectTemplateBeforeAddField"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parentWindow = Window.GetWindow(this);
        var win = new FieldEditWindow
        {
            Owner = parentWindow
        };

        if (win.ShowDialog() != true) return;

        var field = win.Field;
        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && !x.IsDeleted)
            .ToListAsync();

        var duplicateFieldError = GetDuplicateFieldError(fields, field.FieldName, field.FieldCode, null);
        if (duplicateFieldError != null)
        {
            AppMessageBox.Show(duplicateFieldError, AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var maxSort = fields.Count == 0 ? 0 : fields.Max(x => x.Sort);
        field.Id = IdHelper.NewId();
        field.TemplateId = template.Id;
        field.Sort = maxSort + 10;

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Insertable(field).ExecuteCommandAsync();
            await NormalizeFieldSortAsync(template.Id);
            await IncrementTemplateVersionAsync(template.Id);
        });
        await LoadFieldsAsync(field.Id);
    }

    private async void EditField_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateFieldEdit, "编辑字段")) return;

        var template = SelectedTemplate;
        var field = SelectedField;
        if (template == null || field == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectFieldToEdit"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (TemplateSystemFields.IsManagedSystemField(field.FieldCode))
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.ManagedSystemFieldEditDenied"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parentWindow = Window.GetWindow(this);
        var win = new FieldEditWindow(field)
        {
            Owner = parentWindow
        };

        if (win.ShowDialog() != true) return;

        var edited = win.Field;
        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == edited.TemplateId && !x.IsDeleted)
            .ToListAsync();
        var before = fields.FirstOrDefault(x => x.Id == edited.Id);
        if (before == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.FieldMissingRefresh"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            await LoadFieldsAsync();
            return;
        }

        var duplicateFieldError = GetDuplicateFieldError(fields, edited.FieldName, edited.FieldCode, edited.Id);
        if (duplicateFieldError != null)
        {
            AppMessageBox.Show(duplicateFieldError, AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!HasFieldMaintenanceChanges(before, edited))
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.FieldNoChanges"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!string.Equals(before.FieldCode, edited.FieldCode, StringComparison.OrdinalIgnoreCase) &&
            !await ConfirmHistoricalFieldCodeUsageAsync(template.Id, before.FieldCode, "修改字段编码"))
        {
            return;
        }

        var history = NewFieldHistory(
            template,
            before,
            LabelTemplateFieldHistory.OperationUpdate,
            CreateFieldSnapshotJson(before),
            CreateFieldSnapshotJson(edited),
            $"编辑字段：{edited.FieldName}（{edited.FieldCode}）");

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Insertable(history).ExecuteCommandAsync();
            await AppDb.Db.Updateable(edited)
                .UpdateColumns(x => new
                {
                    x.FieldName,
                    x.FieldCode,
                    x.FieldType,
                    x.IsRequired,
                    x.Remark,
                    x.MinLength,
                    x.MaxLength,
                    x.RegexPattern,
                    x.RegexErrorMessage,
                    x.EnumOptions,
                    x.MinValue,
                    x.MaxValue
                })
                .ExecuteCommandAsync();
            await IncrementTemplateVersionAsync(template.Id);
        });

        await LoadFieldsAsync(edited.Id);
    }

    private async void DeleteField_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateFieldDelete, "删除字段")) return;

        var field = SelectedField;
        if (field == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectFieldToDelete"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (TemplateSystemFields.IsManagedSystemField(field.FieldCode))
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.ManagedSystemFieldDeleteDenied"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AppMessageBox.Show(AppLanguageService.Format("Template.DeleteFieldConfirm", field.FieldName), AppLanguageService.GetString("Common.Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var template = SelectedTemplate;
        if (template == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectTemplateOnly"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var beforeList = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.Id == field.Id && !x.IsDeleted)
            .ToListAsync();
        var before = beforeList.FirstOrDefault();
        if (before == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.FieldMissingRefresh"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            await LoadFieldsAsync();
            return;
        }

        if (!await ConfirmHistoricalFieldCodeUsageAsync(template.Id, before.FieldCode, "删除字段"))
            return;

        var history = NewFieldHistory(
            template,
            before,
            LabelTemplateFieldHistory.OperationDelete,
            CreateFieldSnapshotJson(before),
            CreateFieldSnapshotJson(before, true),
            $"删除字段：{before.FieldName}（{before.FieldCode}）");

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Insertable(history).ExecuteCommandAsync();
            before.IsDeleted = true;
            await AppDb.Db.Updateable(before).UpdateColumns(x => x.IsDeleted).ExecuteCommandAsync();
            await NormalizeFieldSortAsync(before.TemplateId);
            await IncrementTemplateVersionAsync(template.Id);
        });

        await LoadFieldsAsync();
    }

    private async void FieldHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateFieldHistory, "字段历史")) return;

        var template = SelectedTemplate;
        if (template == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectTemplateBeforeHistory"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new FieldHistoryWindow(template.Id, template.Name)
        {
            Owner = Window.GetWindow(this)
        };
        var result = win.ShowDialog();
        if (result == true && win.CopiedFieldId.HasValue)
            await LoadFieldsAsync(win.CopiedFieldId.Value);
    }

    #endregion 字段维护 (Dialog 方式)

    private async void UploadTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateUpload, "上传模板")) return;

        var template = SelectedTemplate;
        if (template == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectTemplateOnly"));
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = AppLanguageService.GetString("Template.StiTemplateFilter")
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await RunQueuedAsync(sender, BackgroundTaskKind.Upload, AppLanguageService.GetString("Template.Uploading"), context =>
                UploadTemplateAsync(template, dialog.FileName, context.CancellationToken));
            await LoadTemplatesAsync();
            AppMessageBox.Show(AppLanguageService.GetString("Template.TemplateSaved"));
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(AppLanguageService.Format("Template.TemplateSaveFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateDesign, "设计模板")) return;

        var template = SelectedTemplate;
        if (template == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectTemplateOnly"));
            return;
        }

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .ToListAsync();

        if (fields.Count == 0)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.FieldsRequiredBeforeDesign"));
            return;
        }

        try
        {
            var storage = LabelTemplateStorageFactory.Create(App.Settings.RunMode);
            var designer = new StiTemplateDesignerService(storage);
            await RunQueuedAsync(sender, BackgroundTaskKind.Design, AppLanguageService.GetString("Template.OpeningDesigner"), context =>
                designer.DesignAsync(template, fields, context.CancellationToken));
            await LoadTemplatesAsync();
            AppMessageBox.Show(AppLanguageService.GetString("Template.DesignSaved"));
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(AppLanguageService.Format("Template.OpenDesignerFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FieldGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 移除了表单填充逻辑，选择改变现在仅用于选中数据
    }

    private static byte[] CreateEmptyReportBytes(string dataSourceName, List<LabelTemplateField> fields)
    {
        using (var report = new StiReport())
        {
            report.Dictionary.Databases.Clear();
            var dt = new DataTable(dataSourceName);
            foreach (var f in fields)
            {
                if (!string.IsNullOrWhiteSpace(f.FieldCode))
                    dt.Columns.Add(f.FieldCode, typeof(string));

            }
            report.RegData(dataSourceName, dt);
            report.Dictionary.Synchronize();
            using (var ms = new MemoryStream())
            {
                report.Save(ms);
                return ms.ToArray();
            }
        }
    }

    private async void SeedDemo_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateSeedDemo, "初始化示例数据")) return;

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

        var storageType = App.Settings.RunMode == AppRunMode.LocalSqlite
            ? TemplateStorageType.LocalFile
            : TemplateStorageType.Database;

        // 1. 初始化“产品基础标签” (批次控制模板)
        var template = await FindTemplateAsync(category.Id, "产品基础标签");
        if (template == null)
        {
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
                IsEnabled = true,
                TemplateMode = LabelTemplateMode.Batch,
                IsSerialNumber = false,
                BatchNumberPattern = SerialNumberService.DefaultBatchPattern,
                CurrentSerialValue = 0
            };
            await AppDb.Db.Insertable(template).ExecuteCommandAsync();
        }

        var existsFields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .AnyAsync();
        
        List<LabelTemplateField> fields;
        if (!existsFields)
        {
            fields = new List<LabelTemplateField>
            {
                NewField(template.Id, "批号", TemplateSystemFields.BatchNo, "string", true, 5, "系统自动生成的模板类型固定字段，禁止修改与删除"),
                NewField(template.Id, "产品名称", "ProductName", "string", true, 10, "产品中文名称"),
                NewField(template.Id, "条码", "Barcode", "string", true, 20, "一维码或二维码内容"),
                NewField(template.Id, "规格", "Spec", "string", false, 30, "如 500ml"),
                NewField(template.Id, "数量", "Qty", "int", true, 40, "打印数量或产品数量"),
                NewField(template.Id, "生产日期", "ProduceDate", "date", false, 50, "yyyy-MM-dd")
            };
            await AppDb.Db.Insertable(fields).ExecuteCommandAsync();
        }
        else
        {
            fields = await AppDb.Db.Queryable<LabelTemplateField>().Where(x => x.TemplateId == template.Id && !x.IsDeleted).ToListAsync();
        }

        // 生成物理 MRT 模板文件 (批次)
        var bytes = CreateEmptyReportBytes(template.DataSourceName, fields);
        if (template.StorageType == TemplateStorageType.LocalFile)
        {
            try
            {
                var dir = Path.GetDirectoryName(template.TemplatePath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(template.TemplatePath!, bytes);
            }
            catch { }
        }
        else
        {
            template.TemplateContent = bytes;
            template.TemplateHash = FileHashHelper.GetSha256(bytes);
            await AppDb.Db.Updateable(template).UpdateColumns(x => new { x.TemplateContent, x.TemplateHash }).ExecuteCommandAsync();
        }

        // 2. 初始化“产品序列号标签” (序列号控制模板)
        var template2 = await FindTemplateAsync(category.Id, "产品序列号标签");
        if (template2 == null)
        {
            var templateId = IdHelper.NewId();
            template2 = new LabelTemplate
            {
                Id = templateId,
                CategoryId = category.Id,
                Name = "产品序列号标签",
                StorageType = storageType,
                DataSourceName = "LabelData",
                TemplateFileName = "产品序列号标签.mrt",
                TemplatePath = storageType == TemplateStorageType.LocalFile
                    ? BuildLocalTemplatePath(templateId)
                    : null,
                Version = 1,
                IsEnabled = true,
                TemplateMode = LabelTemplateMode.Serialized,
                IsSerialNumber = true,
                SerialNumberPrefix = null,
                SerialNumberPattern = SerialNumberService.DefaultPattern,
                SerialResetPeriod = SerialResetPeriod.Never,
                CurrentSerialValue = 0
            };
            await AppDb.Db.Insertable(template2).ExecuteCommandAsync();
        }

        var existsFields2 = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template2.Id)
            .AnyAsync();

        List<LabelTemplateField> fields2;
        if (!existsFields2)
        {
            fields2 = new List<LabelTemplateField>
            {
                NewField(template2.Id, "序列号", TemplateSystemFields.SerialNo, "string", true, 5, "系统自动生成的模板类型固定字段，禁止修改与删除"),
                NewField(template2.Id, "产品名称", "ProductName", "string", true, 10, "产品中文名称"),
                NewField(template2.Id, "条码", "Barcode", "string", true, 20, "一维码或二维码内容"),
                NewField(template2.Id, "规格", "Spec", "string", false, 30, "如 500ml"),
                NewField(template2.Id, "数量", "Qty", "int", true, 40, "打印数量或产品数量")
            };
            await AppDb.Db.Insertable(fields2).ExecuteCommandAsync();
        }
        else
        {
            fields2 = await AppDb.Db.Queryable<LabelTemplateField>().Where(x => x.TemplateId == template2.Id && !x.IsDeleted).ToListAsync();
        }

        // 生成物理 MRT 模板文件 (序列号)
        var bytes2 = CreateEmptyReportBytes(template2.DataSourceName, fields2);
        if (template2.StorageType == TemplateStorageType.LocalFile)
        {
            try
            {
                var dir = Path.GetDirectoryName(template2.TemplatePath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(template2.TemplatePath!, bytes2);
            }
            catch { }
        }
        else
        {
            template2.TemplateContent = bytes2;
            template2.TemplateHash = FileHashHelper.GetSha256(bytes2);
            await AppDb.Db.Updateable(template2).UpdateColumns(x => new { x.TemplateContent, x.TemplateHash }).ExecuteCommandAsync();
        }

        // 3. 一键初始化导入批次、行明细、以及已打印的历史 Job (支持即时预览、即时区间补打)
        var hasBatches = await AppDb.Db.Queryable<LabelImportBatch>().AnyAsync();
        if (!hasBatches)
        {
            // 为“产品基础标签”插入一笔批次导入
            var batch1Id = IdHelper.NewId();
            var batch1 = new LabelImportBatch
            {
                Id = batch1Id,
                TemplateId = template.Id,
                TemplateName = template.Name,
                TemplateVersion = template.Version,
                ExcelFileName = "产品基础标签导入_示例.xlsx",
                ExcelFileHash = "SAMPLE_HASH_BATCH",
                TotalRows = 3,
                ValidRows = 3,
                InvalidRows = 0,
                Status = "Imported",
                OperatorName = "管理员",
                BatchNo = "BATCH-20260525",
                ImportTime = DateTime.Now
            };
            await AppDb.Db.Insertable(batch1).ExecuteCommandAsync();

            var rows1 = new List<LabelImportRow>
            {
                new LabelImportRow
                {
                    Id = IdHelper.NewId(),
                    BatchId = batch1Id,
                    TemplateId = template.Id,
                    RowIndex = 1,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        [TemplateSystemFields.BatchNo] = "BATCH-20260525",
                        ["ProductName"] = "感冒灵颗粒",
                        ["Barcode"] = "6901234567890",
                        ["Spec"] = "10g*9袋",
                        ["Qty"] = "5",
                        ["ProduceDate"] = "2026-05-20"
                    }),
                    IsValid = true,
                    IsPrinted = false,
                    CreateTime = DateTime.Now
                },
                new LabelImportRow
                {
                    Id = IdHelper.NewId(),
                    BatchId = batch1Id,
                    TemplateId = template.Id,
                    RowIndex = 2,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        [TemplateSystemFields.BatchNo] = "BATCH-20260525",
                        ["ProductName"] = "阿莫西林胶囊",
                        ["Barcode"] = "6901234567891",
                        ["Spec"] = "0.25g*24粒",
                        ["Qty"] = "10",
                        ["ProduceDate"] = "2026-05-21"
                    }),
                    IsValid = true,
                    IsPrinted = false,
                    CreateTime = DateTime.Now
                },
                new LabelImportRow
                {
                    Id = IdHelper.NewId(),
                    BatchId = batch1Id,
                    TemplateId = template.Id,
                    RowIndex = 3,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        [TemplateSystemFields.BatchNo] = "BATCH-20260525",
                        ["ProductName"] = "布洛芬缓释胶囊",
                        ["Barcode"] = "6901234567892",
                        ["Spec"] = "0.3g*24粒",
                        ["Qty"] = "2",
                        ["ProduceDate"] = "2026-05-22"
                    }),
                    IsValid = true,
                    IsPrinted = false,
                    CreateTime = DateTime.Now
                }
            };
            foreach (var row in rows1)
            {
                row.SearchText = SearchTextBuilder.FromJson(row.RowDataJson);
            }
            await AppDb.Db.Insertable(rows1).ExecuteCommandAsync();

            // 为“产品序列号标签”插入一笔批次导入
            var batch2Id = IdHelper.NewId();
            var batch2 = new LabelImportBatch
            {
                Id = batch2Id,
                TemplateId = template2.Id,
                TemplateName = template2.Name,
                TemplateVersion = template2.Version,
                ExcelFileName = "产品序列号标签导入_示例.xlsx",
                ExcelFileHash = "SAMPLE_HASH_SERIAL",
                TotalRows = 2,
                ValidRows = 2,
                InvalidRows = 0,
                Status = "Imported",
                OperatorName = "管理员",
                BatchNo = "序列号自增",
                ImportTime = DateTime.Now
            };
            await AppDb.Db.Insertable(batch2).ExecuteCommandAsync();

            var rows2 = new List<LabelImportRow>
            {
                new LabelImportRow
                {
                    Id = IdHelper.NewId(),
                    BatchId = batch2Id,
                    TemplateId = template2.Id,
                    RowIndex = 1,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        ["ProductName"] = "华为 Mate 60 Pro",
                        ["Barcode"] = "HUAWEI-M60P",
                        ["Spec"] = "12GB+512GB",
                        ["Qty"] = "1",
                        [TemplateSystemFields.SerialNo] = ""
                    }),
                    IsValid = true,
                    IsPrinted = false,
                    CreateTime = DateTime.Now
                },
                new LabelImportRow
                {
                    Id = IdHelper.NewId(),
                    BatchId = batch2Id,
                    TemplateId = template2.Id,
                    RowIndex = 2,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        ["ProductName"] = "iPhone 15 Pro",
                        ["Barcode"] = "APPLE-I15P",
                        ["Spec"] = "256GB",
                        ["Qty"] = "1",
                        [TemplateSystemFields.SerialNo] = ""
                    }),
                    IsValid = true,
                    IsPrinted = false,
                    CreateTime = DateTime.Now
                }
            };
            foreach (var row in rows2)
            {
                row.SearchText = SearchTextBuilder.FromJson(row.RowDataJson);
            }
            await AppDb.Db.Insertable(rows2).ExecuteCommandAsync();

            // 4. 为“产品序列号标签”插入一笔已打印任务 Job (以供直接测试序列号补打)
            var jobId = IdHelper.NewId();
            var job = new LabelPrintJob
            {
                Id = jobId,
                TemplateId = template2.Id,
                BatchId = batch2Id,
                TemplateName = template2.Name,
                SelectedRowCount = 3,
                PrinterName = "虚拟打印机",
                Status = "Printed",
                OperatorName = "管理员",
                CreateTime = DateTime.Now.AddMinutes(-5),
                PrintTime = DateTime.Now.AddMinutes(-4)
            };
            await AppDb.Db.Insertable(job).ExecuteCommandAsync();

            var jobRows = new List<LabelPrintJobRow>
            {
                new LabelPrintJobRow
                {
                    Id = IdHelper.NewId(),
                    PrintJobId = jobId,
                    ImportRowId = rows2[0].Id,
                    RowIndex = 1,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        ["ProductName"] = "华为 Mate 60 Pro",
                        ["Barcode"] = "HUAWEI-M60P",
                        ["Spec"] = "12GB+512GB",
                        ["Qty"] = "1",
                        [TemplateSystemFields.SerialNo] = "SN-0001"
                    })
                },
                new LabelPrintJobRow
                {
                    Id = IdHelper.NewId(),
                    PrintJobId = jobId,
                    ImportRowId = rows2[0].Id,
                    RowIndex = 1,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        ["ProductName"] = "华为 Mate 60 Pro",
                        ["Barcode"] = "HUAWEI-M60P",
                        ["Spec"] = "12GB+512GB",
                        ["Qty"] = "1",
                        [TemplateSystemFields.SerialNo] = "SN-0002"
                    })
                },
                new LabelPrintJobRow
                {
                    Id = IdHelper.NewId(),
                    PrintJobId = jobId,
                    ImportRowId = rows2[1].Id,
                    RowIndex = 2,
                    RowDataJson = JsonHelper.Serialize(new Dictionary<string, string>
                    {
                        ["ProductName"] = "iPhone 15 Pro",
                        ["Barcode"] = "APPLE-I15P",
                        ["Spec"] = "256GB",
                        ["Qty"] = "1",
                        [TemplateSystemFields.SerialNo] = "SN-0001"
                    })
                }
            };
            foreach (var row in jobRows)
            {
                row.SearchText = SearchTextBuilder.FromJson(row.RowDataJson);
            }
            await AppDb.Db.Insertable(jobRows).ExecuteCommandAsync();

            // 标记对应的 row 为已打印
            foreach (var r in rows2)
            {
                r.IsPrinted = true;
                r.PrintCount = r.RowIndex == 1 ? 2 : 1;
                r.LastPrintTime = DateTime.Now;
            }
            await AppDb.Db.Updateable(rows2).ExecuteCommandAsync();
        }

        await RefreshAllAsync();
        AppMessageBox.Show(AppLanguageService.GetString("Template.DemoSeedCompleted"));
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
        UpdateEmptyStates();
    }

    private void UpdateTemplatePagination()
    {
        if (TemplatePageInfoText == null ||
            TemplateFirstPageButton == null || TemplatePrevPageButton == null ||
            TemplateNextPageButton == null || TemplateLastPageButton == null)
        {
            return;
        }

        TemplatePageInfoText.Text = AppLanguageService.Format("Template.PageInfo", _templateCurrentPage, _templateTotalPages, _templateTotalRows);

        var hasRows = _templateTotalRows > 0;
        TemplateFirstPageButton.IsEnabled = hasRows && _templateCurrentPage > 1;
        TemplatePrevPageButton.IsEnabled = hasRows && _templateCurrentPage > 1;
        TemplateNextPageButton.IsEnabled = hasRows && _templateCurrentPage < _templateTotalPages;
        TemplateLastPageButton.IsEnabled = hasRows && _templateCurrentPage < _templateTotalPages;
    }

    private void UpdateEmptyStates()
    {
        if (CategoryEmptyText != null)
        {
            var categoryCount = CategoryGrid?.ItemsSource?.Cast<object>().Count() ?? 0;
            CategoryEmptyText.Visibility = categoryCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (TemplateEmptyText != null)
        {
            TemplateEmptyText.Text = SelectedCategory == null
                ? AppLanguageService.GetString("Template.SelectCategoryForTemplates")
                : AppLanguageService.GetString("Template.NoTemplatesInCategory");
            TemplateEmptyText.Visibility = _templateTotalRows == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (FieldEmptyText != null)
        {
            var fieldCount = FieldGrid?.ItemsSource?.Cast<object>().Count() ?? 0;
            FieldEmptyText.Text = SelectedTemplate == null
                ? AppLanguageService.GetString("Template.SelectTemplateForFields")
                : AppLanguageService.GetString("Template.NoFieldsInTemplate");
            FieldEmptyText.Visibility = fieldCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
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

    private static bool TryParseTemplateStorageType(string keyword, out TemplateStorageType storageType)
    {
        var normalized = keyword.Trim();
        if (normalized.Contains("本地", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            storageType = TemplateStorageType.LocalFile;
            return true;
        }

        if (normalized.Contains("数据库", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("database", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("db", StringComparison.OrdinalIgnoreCase))
        {
            storageType = TemplateStorageType.Database;
            return true;
        }

        return Enum.TryParse(normalized, true, out storageType);
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

    private async void MoveFieldUp_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateFieldSort, "字段排序")) return;
        await MoveSelectedFieldAsync(-1);
    }

    private async void MoveFieldDown_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.TemplateFieldSort, "字段排序")) return;
        await MoveSelectedFieldAsync(1);
    }

    private async Task MoveSelectedFieldAsync(int direction)
    {
        var template = SelectedTemplate;
        var field = SelectedField;
        if (template == null || field == null)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Template.SelectFieldOnly"));
            return;
        }

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var index = fields.FindIndex(x => x.Id == field.Id);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= fields.Count)
            return;

        (fields[index].Sort, fields[targetIndex].Sort) = (fields[targetIndex].Sort, fields[index].Sort);
        await AppDb.Db.Updateable(new[] { fields[index], fields[targetIndex] }).ExecuteCommandAsync();
        await NormalizeFieldSortAsync(template.Id);
        await LoadFieldsAsync(field.Id);
    }

    private async Task NormalizeFieldSortAsync(long templateId)
    {
        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync();

        for (var i = 0; i < fields.Count; i++)
            fields[i].Sort = (i + 1) * 10;

        if (fields.Count > 0)
            await AppDb.Db.Updateable(fields).ExecuteCommandAsync();
    }

    private static string? GetDuplicateFieldError(IEnumerable<LabelTemplateField> fields, string name, string code, long? excludeFieldId)
    {
        var candidates = excludeFieldId.HasValue
            ? fields.Where(x => x.Id != excludeFieldId.Value)
            : fields;

        if (candidates.Any(x => string.Equals(x.FieldName.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return "字段名已存在，请勿重复新增。";

        if (candidates.Any(x => string.Equals(x.FieldCode.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            return "字段编码已存在，请勿重复新增。";

        return null;
    }

    private static bool HasFieldMaintenanceChanges(LabelTemplateField before, LabelTemplateField after)
    {
        return !string.Equals(before.FieldName, after.FieldName, StringComparison.Ordinal) ||
               !string.Equals(before.FieldCode, after.FieldCode, StringComparison.Ordinal) ||
               !string.Equals(before.FieldType, after.FieldType, StringComparison.Ordinal) ||
               before.IsRequired != after.IsRequired ||
               !string.Equals(before.Remark ?? string.Empty, after.Remark ?? string.Empty, StringComparison.Ordinal) ||
               before.MinLength != after.MinLength ||
               before.MaxLength != after.MaxLength ||
               !string.Equals(before.RegexPattern ?? string.Empty, after.RegexPattern ?? string.Empty, StringComparison.Ordinal) ||
               !string.Equals(before.RegexErrorMessage ?? string.Empty, after.RegexErrorMessage ?? string.Empty, StringComparison.Ordinal) ||
               !string.Equals(before.EnumOptions ?? string.Empty, after.EnumOptions ?? string.Empty, StringComparison.Ordinal) ||
               before.MinValue != after.MinValue ||
               before.MaxValue != after.MaxValue;
    }

    private async Task<bool> ConfirmHistoricalFieldCodeUsageAsync(long templateId, string fieldCode, string actionText)
    {
        if (!await HasHistoricalFieldCodeUsageAsync(templateId, fieldCode))
            return true;

        var result = AppMessageBox.Show(
            $"字段编码“{fieldCode}”已被历史导入或打印数据使用。\n继续{actionText}后，历史数据可能会以废弃字段显示，或影响追溯查看。\n是否继续？",
            "字段编码已被历史数据使用",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private static async Task<bool> HasHistoricalFieldCodeUsageAsync(long templateId, string fieldCode)
    {
        if (string.IsNullOrWhiteSpace(fieldCode))
            return false;

        var keyPattern = $"\"{fieldCode}\"";
        var hasImportRows = await AppDb.Db.Queryable<LabelImportRow>()
            .Where(x =>
                x.TemplateId == templateId &&
                ((x.SearchText != null && x.SearchText.Contains(fieldCode)) ||
                 (x.SearchText == null && x.RowDataJson.Contains(keyPattern))))
            .AnyAsync();
        if (hasImportRows)
            return true;

        var jobIds = await AppDb.Db.Queryable<LabelPrintJob>()
            .Where(x => x.TemplateId == templateId)
            .Select(x => x.Id)
            .ToListAsync();
        if (jobIds.Count == 0)
            return false;

        return await AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x =>
                jobIds.Contains(x.PrintJobId) &&
                ((x.SearchText != null && x.SearchText.Contains(fieldCode)) ||
                 (x.SearchText == null && x.RowDataJson.Contains(keyPattern))))
            .AnyAsync();
    }

    private static LabelTemplateFieldHistory NewFieldHistory(
        LabelTemplate template,
        LabelTemplateField field,
        string operationType,
        string beforeSnapshotJson,
        string? afterSnapshotJson,
        string changeSummary)
    {
        return new LabelTemplateFieldHistory
        {
            Id = IdHelper.NewId(),
            TemplateId = template.Id,
            TemplateName = template.Name,
            FieldId = field.Id,
            OperationType = operationType,
            BeforeSnapshotJson = beforeSnapshotJson,
            AfterSnapshotJson = afterSnapshotJson,
            ChangeSummary = changeSummary,
            OperatorName = CurrentUserService.OperatorName,
            CreateTime = DateTime.Now
        };
    }

    private static string CreateFieldSnapshotJson(LabelTemplateField field, bool? isDeleted = null)
    {
        return JsonHelper.Serialize(new
        {
            field.Id,
            field.TemplateId,
            field.FieldName,
            field.FieldCode,
            field.FieldType,
            field.IsRequired,
            field.Sort,
            field.Remark,
            field.MinLength,
            field.MaxLength,
            field.RegexPattern,
            field.RegexErrorMessage,
            field.EnumOptions,
            field.MinValue,
            field.MaxValue,
            IsDeleted = isDeleted ?? field.IsDeleted
        });
    }

    private static async Task IncrementTemplateVersionAsync(long templateId)
    {
        var templates = await AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.Id == templateId)
            .Take(1)
            .ToListAsync();
        var template = templates.FirstOrDefault()
            ?? throw new InvalidOperationException("模板不存在，无法更新版本。");

        template.Version += 1;
        template.UpdateTime = DateTime.Now;
        await AppDb.Db.Updateable(template)
            .UpdateColumns(x => new { x.Version, x.UpdateTime })
            .ExecuteCommandAsync();
    }

    private async Task RunQueuedAsync(object sender, string runningText, Func<CancellationToken, Task> operation)
    {
        await RunQueuedAsync(sender, BackgroundTaskKind.Other, runningText, context => operation(context.CancellationToken));
    }

    private async Task RunQueuedAsync(object sender, BackgroundTaskKind kind, string runningText, Func<BackgroundTaskContext, Task> operation)
    {
        var element = sender as UIElement;
        var modeText = GetRunModeText();

        if (element != null)
            element.IsEnabled = false;
        RunModeText.Text = $"{modeText} · {runningText}";

        try
        {
            await BackgroundTaskQueue.Shared.EnqueueAsync(kind, runningText, operation);
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
            RunModeText.Text = modeText;
        }
    }

    private void AppLanguageService_LanguageChanged(object? sender, AppLanguage language)
    {
        RunModeText.Text = GetRunModeText();
        CategoryGrid?.Items.Refresh();
        TemplateGrid?.Items.Refresh();
        FieldGrid?.Items.Refresh();
        UpdateEmptyStates();
    }

    private static string GetRunModeText()
    {
        return App.Settings.RunMode switch
        {
            AppRunMode.LocalSqlite => AppLanguageService.GetString("Settings.LocalSqlite"),
            AppRunMode.LanPostgreSql => AppLanguageService.GetString("Settings.LanPostgreSql"),
            _ => App.Settings.RunMode.ToString()
        };
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
