using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Database;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;
using SqlSugar;

namespace LabelPrintClient.Modules.Template.Views;

public partial class FieldHistoryWindow : Window
{
    private const int DefaultHistoryPageSize = 20;

    private readonly long _templateId;
    private bool _isWindowLoaded;
    private bool _isLoading;
    private int _historyCurrentPage = 1;
    private int _historyPageSize = DefaultHistoryPageSize;
    private int _historyTotalRows;
    private int _historyTotalPages = 1;

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
}
