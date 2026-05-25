using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;
using SqlSugar;

namespace LabelPrintClient.Modules.Template.Views;

public partial class FieldHistoryWindow : Window
{
    private const int DefaultHistoryPageSize = 20;
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly long _templateId;
    private bool _isWindowLoaded;
    private bool _isLoading;
    private int _historyCurrentPage = 1;
    private int _historyPageSize = DefaultHistoryPageSize;
    private int _historyTotalRows;
    private int _historyTotalPages = 1;

    public long? CopiedFieldId { get; private set; }

    public FieldHistoryWindow(long templateId, string templateName)
    {
        InitializeComponent();
        _templateId = templateId;
        TemplateNameText.Text = $"模板：{templateName}";
        Loaded += async (_, _) =>
        {
            _isWindowLoaded = true;
            _historyPageSize = GetSelectedHistoryPageSize();
            await LoadHistoryAsync();
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async void CopyHistoryField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LabelTemplateFieldHistory history } ||
            !history.CanCopyAsField)
        {
            return;
        }

        var field = ReadFieldFromSnapshot(history.BeforeSnapshotJson);
        if (field == null)
        {
            AppMessageBox.Show("无法读取字段快照，不能复制。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        field.Id = 0;
        field.TemplateId = _templateId;
        field.IsDeleted = false;
        field.Sort = 0;

        var win = new FieldEditWindow(field, "复制字段")
        {
            Owner = this
        };
        if (win.ShowDialog() != true)
            return;

        var newField = win.Field;
        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == _templateId && !x.IsDeleted)
            .ToListAsync();

        var duplicateFieldError = GetDuplicateFieldError(fields, newField.FieldName, newField.FieldCode);
        if (duplicateFieldError != null)
        {
            AppMessageBox.Show(duplicateFieldError, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var maxSort = fields.Count == 0 ? 0 : fields.Max(x => x.Sort);
        newField.Id = IdHelper.NewId();
        newField.TemplateId = _templateId;
        newField.Sort = maxSort + 10;
        newField.IsDeleted = false;

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Insertable(newField).ExecuteCommandAsync();
            await NormalizeFieldSortAsync(_templateId);
            await IncrementTemplateVersionAsync(_templateId);
        });

        CopiedFieldId = newField.Id;
        DialogResult = true;
        Close();
    }

    private async void RestoreHistoryField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LabelTemplateFieldHistory history } ||
            !history.CanRestoreField)
        {
            return;
        }

        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == _templateId)
            .ToListAsync();
        var field = fields.FirstOrDefault(x => x.Id == history.FieldId);
        if (field == null)
        {
            AppMessageBox.Show("原字段不存在，无法恢复。可以使用复制功能新建字段。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!field.IsDeleted)
        {
            AppMessageBox.Show("该字段当前未删除，无需恢复。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var activeFields = fields.Where(x => !x.IsDeleted && x.Id != field.Id).ToList();
        var duplicateFieldError = GetDuplicateFieldError(activeFields, field.FieldName, field.FieldCode, "恢复");
        if (duplicateFieldError != null)
        {
            AppMessageBox.Show(duplicateFieldError, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AppMessageBox.Show($"确定恢复字段 {field.FieldName}（{field.FieldCode}）？", "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var beforeSnapshotJson = CreateFieldSnapshotJson(field);
        field.IsDeleted = false;
        field.Sort = activeFields.Count == 0 ? 10 : activeFields.Max(x => x.Sort) + 10;
        var restoreHistory = NewFieldHistory(
            history,
            field,
            LabelTemplateFieldHistory.OperationRestore,
            beforeSnapshotJson,
            CreateFieldSnapshotJson(field),
            $"恢复字段：{field.FieldName}（{field.FieldCode}）");

        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Updateable(field)
                .UpdateColumns(x => new { x.IsDeleted, x.Sort })
                .ExecuteCommandAsync();
            await AppDb.Db.Insertable(restoreHistory).ExecuteCommandAsync();
            await NormalizeFieldSortAsync(_templateId);
            await IncrementTemplateVersionAsync(_templateId);
        });

        CopiedFieldId = field.Id;
        DialogResult = true;
        Close();
    }

    private async Task LoadHistoryAsync()
    {
        if (_isLoading)
            return;

        SetHistoryLoading(true);
        try
        {
            var historyQuery = AppDb.Db.Queryable<LabelTemplateFieldHistory>()
                .Where(x => x.TemplateId == _templateId)
                .OrderByDescending(x => x.CreateTime);
            RefAsync<int> totalRowsRef = 0;
            var currentPage = Math.Max(1, _historyCurrentPage);
            var histories = await historyQuery
                .ToPageListAsync(currentPage, _historyPageSize, totalRowsRef);
            var totalRows = totalRowsRef.Value;
            var totalPages = Math.Max(1, (totalRows + _historyPageSize - 1) / _historyPageSize);

            if (currentPage > totalPages)
            {
                currentPage = totalPages;
                totalRowsRef = 0;
                histories = await historyQuery
                    .ToPageListAsync(currentPage, _historyPageSize, totalRowsRef);
                totalRows = totalRowsRef.Value;
                totalPages = Math.Max(1, (totalRows + _historyPageSize - 1) / _historyPageSize);
            }

            _historyTotalRows = totalRows;
            _historyTotalPages = totalPages;
            _historyCurrentPage = currentPage;
            HistoryGrid.ItemsSource = histories;
            UpdateHistoryPagination();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"加载字段履历失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetHistoryLoading(false);
        }
    }

    private async void HistoryFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_historyCurrentPage <= 1) return;
        _historyCurrentPage = 1;
        await LoadHistoryAsync();
    }

    private async void HistoryPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_historyCurrentPage <= 1) return;
        _historyCurrentPage--;
        await LoadHistoryAsync();
    }

    private async void HistoryNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_historyCurrentPage >= _historyTotalPages) return;
        _historyCurrentPage++;
        await LoadHistoryAsync();
    }

    private async void HistoryLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_historyCurrentPage >= _historyTotalPages) return;
        _historyCurrentPage = _historyTotalPages;
        await LoadHistoryAsync();
    }

    private async void HistoryPageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _historyPageSize = GetSelectedHistoryPageSize();
        _historyCurrentPage = 1;
        if (_isWindowLoaded)
            await LoadHistoryAsync();
        else
            UpdateHistoryPagination();
    }

    private void UpdateHistoryPagination()
    {
        if (StatusText == null ||
            HistoryPageInfoText == null ||
            HistoryFirstPageButton == null || HistoryPrevPageButton == null ||
            HistoryNextPageButton == null || HistoryLastPageButton == null)
        {
            return;
        }

        StatusText.Text = $"当前页 {HistoryGrid.Items.Count} 条；共 {_historyTotalRows} 条履历";
        HistoryPageInfoText.Text = $"{_historyCurrentPage} / {_historyTotalPages}";

        var hasRows = _historyTotalRows > 0;
        HistoryFirstPageButton.IsEnabled = !_isLoading && hasRows && _historyCurrentPage > 1;
        HistoryPrevPageButton.IsEnabled = !_isLoading && hasRows && _historyCurrentPage > 1;
        HistoryNextPageButton.IsEnabled = !_isLoading && hasRows && _historyCurrentPage < _historyTotalPages;
        HistoryLastPageButton.IsEnabled = !_isLoading && hasRows && _historyCurrentPage < _historyTotalPages;
    }

    private void SetHistoryLoading(bool isLoading)
    {
        _isLoading = isLoading;

        if (HistoryLoadingOverlay != null)
            HistoryLoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryGrid != null)
            HistoryGrid.IsEnabled = !isLoading;
        if (RefreshButton != null)
            RefreshButton.IsEnabled = !isLoading;
        if (HistoryPageSizeBox != null)
            HistoryPageSizeBox.IsEnabled = !isLoading;

        UpdateHistoryPagination();
    }

    private int GetSelectedHistoryPageSize()
    {
        if (HistoryPageSizeBox?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var pageSize) &&
            pageSize > 0)
        {
            return pageSize;
        }

        return DefaultHistoryPageSize;
    }

    private static LabelTemplateField? ReadFieldFromSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LabelTemplateField>(snapshotJson, SnapshotJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetDuplicateFieldError(IEnumerable<LabelTemplateField> fields, string name, string code, string actionText = "复制")
    {
        if (fields.Any(x => string.Equals(x.FieldName.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return $"字段名已存在，无法{actionText}。";

        if (fields.Any(x => string.Equals(x.FieldCode.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            return $"字段编码已存在，无法{actionText}。";

        return null;
    }

    private static LabelTemplateFieldHistory NewFieldHistory(
        LabelTemplateFieldHistory sourceHistory,
        LabelTemplateField field,
        string operationType,
        string beforeSnapshotJson,
        string? afterSnapshotJson,
        string changeSummary)
    {
        return new LabelTemplateFieldHistory
        {
            Id = IdHelper.NewId(),
            TemplateId = sourceHistory.TemplateId,
            TemplateName = sourceHistory.TemplateName,
            FieldId = field.Id,
            OperationType = operationType,
            BeforeSnapshotJson = beforeSnapshotJson,
            AfterSnapshotJson = afterSnapshotJson,
            ChangeSummary = changeSummary,
            OperatorName = App.Settings.OperatorName,
            CreateTime = DateTime.Now
        };
    }

    private static string CreateFieldSnapshotJson(LabelTemplateField field)
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
            field.IsDeleted
        });
    }

    private static async Task NormalizeFieldSortAsync(long templateId)
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
}
