using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using LabelPrintClient.ViewModels;

namespace LabelPrintClient.Views;

public partial class PrintHistoryView : System.Windows.Controls.UserControl
{
    private const int DefaultJobPageSize = 20;
    private const int DefaultRowPageSize = 50;

    private readonly ObservableCollection<PrintJobGridItem> _jobs = new();
    private readonly ObservableCollection<PrintJobRowGridItem> _rows = new();
    private CancellationTokenSource? _jobLoadCts;
    private CancellationTokenSource? _rowLoadCts;
    private int _jobCurrentPage = 1;
    private int _jobPageSize = DefaultJobPageSize;
    private int _jobTotalRows;
    private int _jobTotalPages = 1;
    private int _rowCurrentPage = 1;
    private int _rowPageSize = DefaultRowPageSize;
    private int _rowTotalRows;
    private int _rowTotalPages = 1;

    public PrintHistoryView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshHistoryAsync();
        Unloaded += (_, _) => CancelPendingLoads();
    }

    private PrintJobGridItem? SelectedJob => JobGrid.SelectedItem as PrintJobGridItem;

    public void RefreshHistory()
    {
        _ = RefreshHistoryAsync();
    }

    public async Task RefreshHistoryAsync()
    {
        _jobCurrentPage = 1;
        await LoadJobsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshHistoryAsync();

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await RefreshHistoryAsync();
    }

    private async Task LoadJobsAsync()
    {
        var status = GetSelectedStatus();
        var token = ResetCancellation(ref _jobLoadCts);

        try
        {
            var jobQuery = AppDb.Db.Queryable<LabelPrintJob>();
            if (!string.IsNullOrWhiteSpace(status))
                jobQuery = jobQuery.Where(x => x.Status == status);

            var totalRows = await jobQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)_jobPageSize));
            var currentPage = Math.Clamp(_jobCurrentPage, 1, totalPages);

            var pageJobs = (await jobQuery
                    .OrderByDescending(x => x.CreateTime)
                    .OrderByDescending(x => x.Id)
                    .Skip((currentPage - 1) * _jobPageSize)
                    .Take(_jobPageSize)
                    .ToListAsync())
                .Select(PrintJobGridItem.From)
                .ToList();

            if (token.IsCancellationRequested || GetSelectedStatus() != status) return;

            _jobTotalRows = totalRows;
            _jobTotalPages = totalPages;
            _jobCurrentPage = currentPage;

            _jobs.Clear();
            foreach (var item in pageJobs)
            {
                _jobs.Add(item);
            }

            JobGrid.ItemsSource = _jobs;
            UpdateJobPagination();

            if (_jobs.Count == 0)
            {
                JobGrid.SelectedItem = null;
                ClearRows();
                return;
            }

            JobGrid.SelectedIndex = 0;
            await LoadRowsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"加载打印记录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void JobGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async Task LoadRowsAsync()
    {
        var job = SelectedJob;
        if (job == null)
        {
            ClearRows();
            return;
        }

        var token = ResetCancellation(ref _rowLoadCts);
        try
        {
            var fields = await AppDb.Db.Queryable<LabelTemplateField>()
                .Where(x => x.TemplateId == job.TemplateId)
                .OrderBy(x => x.Sort)
                .ToListAsync();

            var rowQuery = AppDb.Db.Queryable<LabelPrintJobRow>()
                .Where(x => x.PrintJobId == job.Id);

            var totalRows = await rowQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)_rowPageSize));
            var currentPage = Math.Clamp(_rowCurrentPage, 1, totalPages);

            var pageRows = (await rowQuery
                    .OrderBy(x => x.RowIndex)
                    .Skip((currentPage - 1) * _rowPageSize)
                    .Take(_rowPageSize)
                    .ToListAsync())
                .Select(x => new PrintJobRowGridItem
                {
                    Id = x.Id,
                    ImportRowId = x.ImportRowId,
                    RowIndex = x.RowIndex,
                    Data = JsonHelper.Deserialize<Dictionary<string, string>>(x.RowDataJson) ?? new Dictionary<string, string>()
                })
                .ToList();

            if (token.IsCancellationRequested || SelectedJob?.Id != job.Id) return;

            _rowTotalRows = totalRows;
            _rowTotalPages = totalPages;
            _rowCurrentPage = currentPage;

            BuildRowGridColumns(fields, pageRows);
            _rows.Clear();
            foreach (var item in pageRows)
            {
                _rows.Add(item);
            }

            RowGrid.ItemsSource = _rows;
            UpdateRowPagination();
            UpdateSummary();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"加载打印明细失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildRowGridColumns(IReadOnlyList<LabelTemplateField> fields, IReadOnlyList<PrintJobRowGridItem> rows)
    {
        RowGrid.Columns.Clear();
        RowGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Excel行",
            Binding = new System.Windows.Data.Binding(nameof(PrintJobRowGridItem.RowIndex)),
            Width = 80,
            IsReadOnly = true
        });

        var knownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            knownCodes.Add(field.FieldCode);
            RowGrid.Columns.Add(new DataGridTextColumn
            {
                Header = field.FieldName,
                Binding = new System.Windows.Data.Binding($"Data[{field.FieldCode}]"),
                Width = 150,
                IsReadOnly = true
            });
        }

        var extraKeys = rows
            .SelectMany(x => x.Data.Keys)
            .Where(x => !knownCodes.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        foreach (var key in extraKeys)
        {
            RowGrid.Columns.Add(new DataGridTextColumn
            {
                Header = key,
                Binding = new System.Windows.Data.Binding($"Data[{key}]"),
                Width = 150,
                IsReadOnly = true
            });
        }
    }

    private void ClearRows()
    {
        _rows.Clear();
        _rowCurrentPage = 1;
        _rowTotalRows = 0;
        _rowTotalPages = 1;
        RowGrid.Columns.Clear();
        RowGrid.ItemsSource = _rows;
        UpdateRowPagination();
        UpdateSummary();
    }

    private async void JobFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage <= 1) return;
        _jobCurrentPage = 1;
        await LoadJobsAsync();
    }

    private async void JobPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage <= 1) return;
        _jobCurrentPage--;
        await LoadJobsAsync();
    }

    private async void JobNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage >= _jobTotalPages) return;
        _jobCurrentPage++;
        await LoadJobsAsync();
    }

    private async void JobLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage >= _jobTotalPages) return;
        _jobCurrentPage = _jobTotalPages;
        await LoadJobsAsync();
    }

    private async void JobPageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _jobPageSize = GetSelectedJobPageSize();
        _jobCurrentPage = 1;
        if (IsLoaded)
            await LoadJobsAsync();
    }

    private async void RowFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async void RowPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage--;
        await LoadRowsAsync();
    }

    private async void RowNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage++;
        await LoadRowsAsync();
    }

    private async void RowLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage = _rowTotalPages;
        await LoadRowsAsync();
    }

    private async void RowPageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowPageSize = GetSelectedRowPageSize();
        _rowCurrentPage = 1;
        if (IsLoaded)
            await LoadRowsAsync();
    }

    private void UpdateJobPagination()
    {
        if (JobPageInfoText == null ||
            JobFirstPageButton == null || JobPrevPageButton == null ||
            JobNextPageButton == null || JobLastPageButton == null)
        {
            return;
        }

        JobPageInfoText.Text = $"{_jobCurrentPage} / {_jobTotalPages}，共 {_jobTotalRows} 条";

        var hasRows = _jobTotalRows > 0;
        JobFirstPageButton.IsEnabled = hasRows && _jobCurrentPage > 1;
        JobPrevPageButton.IsEnabled = hasRows && _jobCurrentPage > 1;
        JobNextPageButton.IsEnabled = hasRows && _jobCurrentPage < _jobTotalPages;
        JobLastPageButton.IsEnabled = hasRows && _jobCurrentPage < _jobTotalPages;
    }

    private void UpdateRowPagination()
    {
        if (RowPageInfoText == null ||
            RowFirstPageButton == null || RowPrevPageButton == null ||
            RowNextPageButton == null || RowLastPageButton == null)
        {
            return;
        }

        RowPageInfoText.Text = $"{_rowCurrentPage} / {_rowTotalPages}，共 {_rowTotalRows} 行";

        var hasRows = _rowTotalRows > 0;
        RowFirstPageButton.IsEnabled = hasRows && _rowCurrentPage > 1;
        RowPrevPageButton.IsEnabled = hasRows && _rowCurrentPage > 1;
        RowNextPageButton.IsEnabled = hasRows && _rowCurrentPage < _rowTotalPages;
        RowLastPageButton.IsEnabled = hasRows && _rowCurrentPage < _rowTotalPages;
    }

    private void UpdateSummary()
    {
        if (HistorySummaryText == null)
            return;

        var selectedJobText = SelectedJob == null
            ? "未选择打印任务"
            : $"当前任务：{SelectedJob.TemplateName}，{SelectedJob.SelectedRowCount} 张，状态 {SelectedJob.StatusText}";
        HistorySummaryText.Text = $"打印任务共 {_jobTotalRows} 条；当前明细 {_rows.Count} 行 / 共 {_rowTotalRows} 行。{selectedJobText}。";
    }

    private string? GetSelectedStatus()
    {
        if (StatusFilterBox?.SelectedItem is ComboBoxItem item)
        {
            var status = item.Tag?.ToString();
            return string.IsNullOrWhiteSpace(status) ? null : status;
        }

        return null;
    }

    private int GetSelectedJobPageSize()
    {
        if (JobPageSizeBox?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var pageSize) &&
            pageSize > 0)
        {
            return pageSize;
        }

        return DefaultJobPageSize;
    }

    private int GetSelectedRowPageSize()
    {
        if (RowPageSizeBox?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var pageSize) &&
            pageSize > 0)
        {
            return pageSize;
        }

        return DefaultRowPageSize;
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
        CancelAndDispose(ref _jobLoadCts);
        CancelAndDispose(ref _rowLoadCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
