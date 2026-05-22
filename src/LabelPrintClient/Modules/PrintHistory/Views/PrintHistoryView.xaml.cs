using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.PrintCenter.ViewModels;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.PrintHistory.Views;

public partial class PrintHistoryView : System.Windows.Controls.UserControl
{
    private const int DefaultJobPageSize = 20;
    private const int DefaultRowPageSize = 50;
    private const int MaxPrintCopies = 999;

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
    private bool _printersLoaded;
    private string? _rowGridColumnSignature;

    public PrintHistoryView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadPrinters();
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

    private async void JobFilter_Click(object sender, RoutedEventArgs e) => await RefreshHistoryAsync();

    private async void ClearJobFilter_Click(object sender, RoutedEventArgs e)
    {
        JobSearchBox.Text = string.Empty;
        await RefreshHistoryAsync();
    }

    private async void JobSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        await RefreshHistoryAsync();
    }

    private async void RowFilter_Click(object sender, RoutedEventArgs e)
    {
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async void ClearRowFilter_Click(object sender, RoutedEventArgs e)
    {
        RowSearchBox.Text = string.Empty;
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async void RowSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async Task LoadJobsAsync()
    {
        var status = GetSelectedStatus();
        var keyword = JobSearchBox?.Text.Trim() ?? string.Empty;
        var token = ResetCancellation(ref _jobLoadCts);

        if (JobLoadingOverlay != null)
        {
            JobLoadingOverlay.Visibility = Visibility.Visible;
        }

        try
        {
            var jobQuery = AppDb.Db.Queryable<LabelPrintJob>();
            if (!string.IsNullOrWhiteSpace(status))
                jobQuery = jobQuery.Where(x => x.Status == status);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                jobQuery = jobQuery.Where(x =>
                    x.TemplateName.Contains(keyword) ||
                    (x.PrinterName != null && x.PrinterName.Contains(keyword)) ||
                    (x.OperatorName != null && x.OperatorName.Contains(keyword)) ||
                    (x.ErrorMessage != null && x.ErrorMessage.Contains(keyword)));
            }

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

            if (token.IsCancellationRequested ||
                GetSelectedStatus() != status ||
                (JobSearchBox?.Text.Trim() ?? string.Empty) != keyword)
            {
                return;
            }

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
            UpdateEmptyStates();

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
            AppMessageBox.Show($"加载打印记录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (JobLoadingOverlay != null)
            {
                JobLoadingOverlay.Visibility = Visibility.Collapsed;
            }
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

        var keyword = RowSearchBox?.Text.Trim() ?? string.Empty;
        var token = ResetCancellation(ref _rowLoadCts);

        if (RowLoadingOverlay != null)
        {
            RowLoadingOverlay.Visibility = Visibility.Visible;
        }

        try
        {
            var fields = await AppDb.Db.Queryable<LabelTemplateField>()
                .Where(x => x.TemplateId == job.TemplateId)
                .OrderBy(x => x.Sort)
                .ToListAsync();

            var rowQuery = AppDb.Db.Queryable<LabelPrintJobRow>()
                .Where(x => x.PrintJobId == job.Id);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                if (int.TryParse(keyword, out var rowIndex))
                    rowQuery = rowQuery.Where(x => x.RowIndex == rowIndex || x.RowDataJson.Contains(keyword));
                else
                    rowQuery = rowQuery.Where(x => x.RowDataJson.Contains(keyword));
            }

            var totalRows = await rowQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)_rowPageSize));
            var currentPage = Math.Clamp(_rowCurrentPage, 1, totalPages);

            var dbRows = await rowQuery
                    .OrderBy(x => x.RowIndex)
                    .Skip((currentPage - 1) * _rowPageSize)
                    .Take(_rowPageSize)
                    .ToListAsync();

            var rowLoadResult = await Task.Run(() =>
            {
                var rows = dbRows.Select(x => new PrintJobRowGridItem
                {
                    Id = x.Id,
                    ImportRowId = x.ImportRowId,
                    RowIndex = x.RowIndex,
                    Data = GridRowDataHelper.Deserialize(x.RowDataJson)
                })
                .ToList();

                GridRowDataHelper.EnsureFieldKeys(rows.Select(x => x.Data), fields);
                var knownCodes = fields
                    .Select(x => x.FieldCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var extraKeys = rows
                    .SelectMany(x => x.Data.Keys)
                    .Where(x => !knownCodes.Contains(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
                GridRowDataHelper.EnsureKeys(rows.Select(x => x.Data), extraKeys);
                return (Rows: rows, ExtraKeys: extraKeys);
            }, token);

            if (token.IsCancellationRequested ||
                SelectedJob?.Id != job.Id ||
                (RowSearchBox?.Text.Trim() ?? string.Empty) != keyword)
            {
                return;
            }

            _rowTotalRows = totalRows;
            _rowTotalPages = totalPages;
            _rowCurrentPage = currentPage;

            BuildRowGridColumnsIfNeeded(fields, rowLoadResult.ExtraKeys);
            _rows.Clear();
            foreach (var item in rowLoadResult.Rows)
            {
                _rows.Add(item);
            }

            RowGrid.ItemsSource = _rows;
            UpdateRowPagination();
            UpdateSummary();
            UpdateEmptyStates();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"加载打印明细失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (RowLoadingOverlay != null)
            {
                RowLoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void BuildRowGridColumnsIfNeeded(IReadOnlyList<LabelTemplateField> fields, IReadOnlyList<string> extraKeys)
    {
        var signature = $"{BuildFieldSignature(fields)}||{string.Join("|", extraKeys)}";
        if (string.Equals(_rowGridColumnSignature, signature, StringComparison.Ordinal))
            return;

        _rowGridColumnSignature = signature;
        BuildRowGridColumns(fields, extraKeys);
    }

    private static string BuildFieldSignature(IEnumerable<LabelTemplateField> fields)
    {
        return string.Join("|", fields.Select(x => $"{x.Id}:{x.Sort}:{x.FieldCode}:{x.FieldName}"));
    }

    private void BuildRowGridColumns(IReadOnlyList<LabelTemplateField> fields, IEnumerable<string> extraKeys)
    {
        RowGrid.Columns.Clear();

        RowGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "操作",
            Width = DataGridLength.Auto,
            CellTemplate = BuildRowActionTemplate()
        });
        RowGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Excel行号",
            Binding = new System.Windows.Data.Binding(nameof(PrintJobRowGridItem.RowIndex)),
            Width = 100,
            IsReadOnly = true
        });

        foreach (var field in fields)
        {
            RowGrid.Columns.Add(new DataGridTextColumn
            {
                Header = field.FieldName,
                Binding = new System.Windows.Data.Binding($"Data[{field.FieldCode}]"),
                Width = 150,
                IsReadOnly = true
            });
        }

        foreach (var key in extraKeys)
        {
            var headerBlock = new TextBlock
            {
                Text = $"{key} (已废弃)",
                ToolTip = $"字段“{key}”在当前最新模板中已被移除，此处仅用于追溯历史打印数据。",
                FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Gray
            };

            var cellStyle = new Style(typeof(TextBlock));
            cellStyle.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
            cellStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray));
            cellStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, $"字段“{key}”在当前最新模板中已被移除，此处仅用于追溯历史打印数据。"));

            RowGrid.Columns.Add(new DataGridTextColumn
            {
                Header = headerBlock,
                Binding = new System.Windows.Data.Binding($"Data[{key}]"),
                Width = 170,
                IsReadOnly = true,
                ElementStyle = cellStyle
            });
        }
    }

    private void ClearRows()
    {
        _rows.Clear();
        _rowCurrentPage = 1;
        _rowTotalRows = 0;
        _rowTotalPages = 1;
        _rowGridColumnSignature = null;
        RowGrid.Columns.Clear();
        RowGrid.ItemsSource = _rows;
        UpdateRowPagination();
        UpdateSummary();
        UpdateEmptyStates();
    }

    private DataTemplate BuildRowActionTemplate()
    {
        var button = new FrameworkElementFactory(typeof(System.Windows.Controls.Button));
        button.SetValue(System.Windows.FrameworkElement.HeightProperty, 28.0);
        button.SetValue(System.Windows.FrameworkElement.StyleProperty, System.Windows.Application.Current.FindResource("ButtonPrimary"));
        button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new System.Windows.RoutedEventHandler(ReprintJobRow_Click));
        button.AppendChild(BuildButtonContent("重打", MahApps.Metro.IconPacks.PackIconMaterialKind.PrinterAlert));

        return new System.Windows.DataTemplate
        {
            VisualTree = button
        };
    }

    private static FrameworkElementFactory BuildButtonContent(string content, MahApps.Metro.IconPacks.PackIconMaterialKind iconKind)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);

        var icon = new FrameworkElementFactory(typeof(MahApps.Metro.IconPacks.PackIconMaterial));
        icon.SetValue(MahApps.Metro.IconPacks.PackIconMaterial.KindProperty, iconKind);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(icon);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, content);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(text);

        return panel;
    }

    private async void ReprintJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PrintJobGridItem job })
            return;
        if (!TryGetPrintOptions(out var printerName, out var printCopies))
            return;

        await ExecuteHistoryPrintAsync(
            sender,
            $"确定重打印任务 {job.TemplateName} 的历史数据，{printCopies} 份？",
            context => new LabelPrintService(App.Settings).ReprintJobAsync(
                job.Id,
                printerName,
                printCopies,
                context.CancellationToken,
                context.Progress));
    }

    private async void RetryJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PrintJobGridItem job })
            return;

        if (!string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            AppMessageBox.Show("只有失败的打印任务可以失败重试。");
            return;
        }
        if (!TryGetPrintOptions(out var printerName, out var printCopies))
            return;

        await ExecuteHistoryPrintAsync(
            sender,
            $"确定重试失败任务 {job.TemplateName}，{printCopies} 份？请确认现场没有重复出纸。",
            context => new LabelPrintService(App.Settings).ReprintJobAsync(
                job.Id,
                printerName,
                printCopies,
                context.CancellationToken,
                context.Progress));
    }

    private async void ReprintJobRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PrintJobRowGridItem row })
            return;
        if (!TryGetPrintOptions(out var printerName, out var printCopies))
            return;

        await ExecuteHistoryPrintAsync(
            sender,
            $"确定重打印 Excel 第 {row.RowIndex} 行，{printCopies} 份？",
            context => new LabelPrintService(App.Settings).ReprintJobRowAsync(
                row.Id,
                printerName,
                printCopies,
                context.CancellationToken,
                context.Progress));
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

    private void LoadPrinters()
    {
        if (_printersLoaded)
            return;

        _printersLoaded = true;
        var printerNames = PrinterSettings.InstalledPrinters
            .Cast<string>()
            .OrderBy(x => x)
            .ToList();

        PrinterNameBox.ItemsSource = printerNames;
        PrintCopiesBox.Text = Math.Clamp(App.Settings.DefaultPrintCopies, 1, MaxPrintCopies).ToString();

        if (!string.IsNullOrWhiteSpace(App.Settings.DefaultPrinterName) &&
            printerNames.Contains(App.Settings.DefaultPrinterName))
        {
            PrinterNameBox.SelectedItem = App.Settings.DefaultPrinterName;
            return;
        }

        var defaultPrinter = new PrinterSettings().PrinterName;
        if (!string.IsNullOrWhiteSpace(defaultPrinter) && printerNames.Contains(defaultPrinter))
            PrinterNameBox.SelectedItem = defaultPrinter;
        else if (printerNames.Count > 0)
            PrinterNameBox.SelectedIndex = 0;
    }

    private bool TryGetPrintOptions(out string printerName, out int printCopies)
    {
        printerName = (PrinterNameBox.SelectedItem as string)?.Trim() ?? string.Empty;
        printCopies = 1;

        if (string.IsNullOrWhiteSpace(printerName))
        {
            AppMessageBox.Show("请先选择打印机。");
            return false;
        }

        if (!int.TryParse(PrintCopiesBox.Text.Trim(), out var copies) ||
            copies < 1 ||
            copies > MaxPrintCopies)
        {
            AppMessageBox.Show($"打印份数必须是 1 到 {MaxPrintCopies} 之间的整数。");
            return false;
        }

        printCopies = copies;
        return true;
    }

    private async Task ExecuteHistoryPrintAsync(
        object sender,
        string confirmMessage,
        Func<BackgroundTaskContext, Task> operation)
    {
        if (App.Settings.ConfirmBeforePrint &&
            AppMessageBox.Show(confirmMessage, "确认打印", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var element = sender as UIElement;
        if (element != null)
            element.IsEnabled = false;

        try
        {
            await BackgroundTaskQueue.Shared.EnqueueAsync(BackgroundTaskKind.Print, "正在提交历史打印...", operation);
            AppMessageBox.Show("打印任务已完成。");
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"打印失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
        }
    }

    private void UpdateEmptyStates()
    {
        if (JobEmptyText != null)
            JobEmptyText.Visibility = _jobTotalRows == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (RowEmptyText != null)
        {
            RowEmptyText.Text = SelectedJob == null
                ? "请选择打印任务查看明细"
                : "当前打印任务没有明细";
            RowEmptyText.Visibility = _rowTotalRows == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
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
