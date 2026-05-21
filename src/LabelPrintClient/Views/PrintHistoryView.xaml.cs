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
        Loaded += (_, _) => RefreshHistory();
    }

    private PrintJobGridItem? SelectedJob => JobGrid.SelectedItem as PrintJobGridItem;

    public void RefreshHistory()
    {
        _jobCurrentPage = 1;
        LoadJobs();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshHistory();
    }

    private void LoadJobs()
    {
        var jobQuery = AppDb.Db.Queryable<LabelPrintJob>();
        var status = GetSelectedStatus();
        if (!string.IsNullOrWhiteSpace(status))
            jobQuery = jobQuery.Where(x => x.Status == status);

        _jobTotalRows = jobQuery.Count();
        _jobTotalPages = Math.Max(1, (int)Math.Ceiling(_jobTotalRows / (double)_jobPageSize));
        if (_jobCurrentPage > _jobTotalPages) _jobCurrentPage = _jobTotalPages;
        if (_jobCurrentPage < 1) _jobCurrentPage = 1;

        var pageJobs = jobQuery
            .OrderByDescending(x => x.CreateTime)
            .OrderByDescending(x => x.Id)
            .Skip((_jobCurrentPage - 1) * _jobPageSize)
            .Take(_jobPageSize)
            .ToList()
            .Select(PrintJobGridItem.From)
            .ToList();

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
        LoadRows();
    }

    private void JobGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowCurrentPage = 1;
        LoadRows();
    }

    private void LoadRows()
    {
        var job = SelectedJob;
        if (job == null)
        {
            ClearRows();
            return;
        }

        var fields = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == job.TemplateId)
            .OrderBy(x => x.Sort)
            .ToList();

        var rowQuery = AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.PrintJobId == job.Id);

        _rowTotalRows = rowQuery.Count();
        _rowTotalPages = Math.Max(1, (int)Math.Ceiling(_rowTotalRows / (double)_rowPageSize));
        if (_rowCurrentPage > _rowTotalPages) _rowCurrentPage = _rowTotalPages;
        if (_rowCurrentPage < 1) _rowCurrentPage = 1;

        var pageRows = rowQuery
            .OrderBy(x => x.RowIndex)
            .Skip((_rowCurrentPage - 1) * _rowPageSize)
            .Take(_rowPageSize)
            .ToList()
            .Select(x => new PrintJobRowGridItem
            {
                Id = x.Id,
                ImportRowId = x.ImportRowId,
                RowIndex = x.RowIndex,
                Data = JsonHelper.Deserialize<Dictionary<string, string>>(x.RowDataJson) ?? new Dictionary<string, string>()
            })
            .ToList();

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

    private void JobFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage <= 1) return;
        _jobCurrentPage = 1;
        LoadJobs();
    }

    private void JobPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage <= 1) return;
        _jobCurrentPage--;
        LoadJobs();
    }

    private void JobNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage >= _jobTotalPages) return;
        _jobCurrentPage++;
        LoadJobs();
    }

    private void JobLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_jobCurrentPage >= _jobTotalPages) return;
        _jobCurrentPage = _jobTotalPages;
        LoadJobs();
    }

    private void JobPageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _jobPageSize = GetSelectedJobPageSize();
        _jobCurrentPage = 1;
        if (IsLoaded)
            LoadJobs();
    }

    private void RowFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage = 1;
        LoadRows();
    }

    private void RowPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage--;
        LoadRows();
    }

    private void RowNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage++;
        LoadRows();
    }

    private void RowLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage = _rowTotalPages;
        LoadRows();
    }

    private void RowPageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowPageSize = GetSelectedRowPageSize();
        _rowCurrentPage = 1;
        if (IsLoaded)
            LoadRows();
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
}
