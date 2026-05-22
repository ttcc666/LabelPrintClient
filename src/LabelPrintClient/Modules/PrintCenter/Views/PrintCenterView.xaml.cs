using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.PrintCenter.ViewModels;
using LabelPrintClient.Services;
using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Views;

public partial class PrintCenterView : System.Windows.Controls.UserControl
{
    private const int DefaultBatchPageSize = 20;
    private const int DefaultRowPageSize = 50;
    private const int MaxPrintCopies = 999;

    private readonly ObservableCollection<ImportRowGridItem> _rows = new();
    private readonly List<LabelTemplate> _allTemplates = new();
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _templateLoadCts;
    private CancellationTokenSource? _batchLoadCts;
    private CancellationTokenSource? _rowLoadCts;
    private int _batchCurrentPage = 1;
    private int _batchPageSize = DefaultBatchPageSize;
    private int _batchTotalRows;
    private int _batchTotalPages = 1;
    private int _rowCurrentPage = 1;
    private int _rowPageSize = DefaultRowPageSize;
    private int _rowTotalRows;
    private int _rowTotalPages = 1;
    private bool _printersLoaded;
    private bool _isBulkSelectingRows;
    private string? _rowGridColumnSignature;

    public PrintCenterView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            LoadPrinters();
            await RefreshAllAsync();
        };
        Unloaded += (_, _) => CancelPendingLoads();
    }

    private LabelCategory? SelectedCategory => CategoryBox.SelectedItem as LabelCategory;
    private LabelTemplate? SelectedTemplate => TemplateBox.SelectedItem as LabelTemplate;
    private LabelImportBatch? SelectedBatch => BatchGrid.SelectedItem as LabelImportBatch;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

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

        var defaultPrinter = new PrinterSettings().PrinterName;
        if (!string.IsNullOrWhiteSpace(App.Settings.DefaultPrinterName) &&
            printerNames.Contains(App.Settings.DefaultPrinterName))
        {
            PrinterNameBox.SelectedItem = App.Settings.DefaultPrinterName;
            return;
        }

        var preferredPrinter = printerNames.FirstOrDefault(x => !IsVirtualDocumentPrinter(x));

        if (!string.IsNullOrWhiteSpace(defaultPrinter) &&
            printerNames.Contains(defaultPrinter) &&
            (!IsVirtualDocumentPrinter(defaultPrinter) || string.IsNullOrWhiteSpace(preferredPrinter)))
        {
            PrinterNameBox.SelectedItem = defaultPrinter;
        }
        else if (!string.IsNullOrWhiteSpace(preferredPrinter))
        {
            PrinterNameBox.SelectedItem = preferredPrinter;
        }
        else if (printerNames.Count > 0)
        {
            PrinterNameBox.SelectedIndex = 0;
        }
    }

    private static bool IsVirtualDocumentPrinter(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return false;

        var name = printerName.ToUpperInvariant();
        return name.Contains("PDF") ||
               name.Contains("XPS") ||
               name.Contains("ONENOTE");
    }

    private async Task RefreshAllAsync()
    {
        var token = ResetCancellation(ref _refreshCts);
        try
        {
            var categories = await AppDb.Db.Queryable<LabelCategory>()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.Sort)
                .ToListAsync();

            if (token.IsCancellationRequested) return;

            CategoryBox.ItemsSource = categories;
            _allTemplates.Clear();
            TemplateBox.ItemsSource = null;
            ClearBatches();
            ClearRows();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"刷新失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var category = SelectedCategory;
        if (category == null)
        {
            _allTemplates.Clear();
            TemplateBox.ItemsSource = null;
            ClearBatches();
            ClearRows();
            return;
        }

        var token = ResetCancellation(ref _templateLoadCts);
        _allTemplates.Clear();
        TemplateBox.ItemsSource = null;
        ClearBatches();
        ClearRows();

        try
        {
            var templates = await AppDb.Db.Queryable<LabelTemplate>()
                .Where(x => x.CategoryId == category.Id && x.IsEnabled)
                .OrderBy(x => x.Name)
                .ToListAsync();

            if (token.IsCancellationRequested || SelectedCategory?.Id != category.Id) return;
            _allTemplates.Clear();
            _allTemplates.AddRange(templates);
            TemplateBox.ItemsSource = _allTemplates.ToList();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"加载模板失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _batchCurrentPage = 1;
        await LoadBatchesAsync();
    }

    private async void LoadBatches_Click(object sender, RoutedEventArgs e) => await LoadBatchesAsync();

    private async void BatchFilter_Click(object sender, RoutedEventArgs e)
    {
        _batchCurrentPage = 1;
        await LoadBatchesAsync();
    }

    private async void BatchStatusFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        _batchCurrentPage = 1;
        await LoadBatchesAsync();
    }

    private async void BatchSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;

        _batchCurrentPage = 1;
        await LoadBatchesAsync();
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

    private async Task LoadBatchesAsync()
    {
        var template = SelectedTemplate;
        if (template == null)
        {
            ClearBatches();
            ClearRows();
            return;
        }

        var token = ResetCancellation(ref _batchLoadCts);
        try
        {
            BatchLoadingOverlay.Visibility = Visibility.Visible;
            var keyword = BatchSearchBox?.Text.Trim() ?? string.Empty;
            var statusFilter = GetSelectedBatchStatusFilter();
            var batchQuery = AppDb.Db.Queryable<LabelImportBatch>()
                .Where(x => x.TemplateId == template.Id);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                batchQuery = batchQuery.Where(x =>
                    x.ExcelFileName.Contains(keyword) ||
                    (x.OperatorName != null && x.OperatorName.Contains(keyword)));
            }

            if (string.Equals(statusFilter, "Active", StringComparison.OrdinalIgnoreCase))
                batchQuery = batchQuery.Where(x => x.Status != "Voided");
            else if (!string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
                batchQuery = batchQuery.Where(x => x.Status == statusFilter);

            RefAsync<int> totalRowsRef = 0;
            var currentPage = Math.Max(1, _batchCurrentPage);
            var batches = await batchQuery
                .OrderByDescending(x => x.ImportTime)
                .OrderByDescending(x => x.Id)
                .ToPageListAsync(currentPage, _batchPageSize, totalRowsRef);
            var totalRows = totalRowsRef.Value;
            var totalPages = Math.Max(1, (totalRows + _batchPageSize - 1) / _batchPageSize);

            if (currentPage > totalPages)
            {
                currentPage = totalPages;
                totalRowsRef = 0;
                batches = await batchQuery
                    .OrderByDescending(x => x.ImportTime)
                    .OrderByDescending(x => x.Id)
                    .ToPageListAsync(currentPage, _batchPageSize, totalRowsRef);
                totalRows = totalRowsRef.Value;
                totalPages = Math.Max(1, (totalRows + _batchPageSize - 1) / _batchPageSize);
            }

            if (token.IsCancellationRequested || SelectedTemplate?.Id != template.Id) return;

            _batchTotalRows = totalRows;
            _batchTotalPages = totalPages;
            _batchCurrentPage = currentPage;
            BatchGrid.ItemsSource = batches;
            ClearRows();
            UpdateBatchPagination();
            UpdateBatchEmptyState();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"加载批次失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BatchLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void DownloadExcel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate == null)
        {
            AppMessageBox.Show("请先选择模板。");
            return;
        }

        var templateId = SelectedTemplate.Id;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            FileName = $"{SelectedTemplate.Name}_导入模板.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await RunQueuedAsync(sender, BackgroundTaskKind.Export, "正在生成 Excel 模板...", context =>
                new ExcelTemplateExportService().ExportAsync(templateId, dialog.FileName, context.CancellationToken));
            AppMessageBox.Show("Excel 模板已生成。");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate == null)
        {
            AppMessageBox.Show("请先选择模板。");
            return;
        }

        var templateId = SelectedTemplate.Id;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel 文件|*.xlsx;*.xlsm|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var service = new LabelImportService();
            var preview = await RunQueuedAsync(sender, BackgroundTaskKind.Import, "正在读取 Excel 数据...", context =>
                service.PreviewExcelAsync(templateId, dialog.FileName, context.CancellationToken));

            var previewWindow = new ImportPreviewWindow(preview)
            {
                Owner = Window.GetWindow(this)
            };

            if (previewWindow.ShowDialog() != true)
            {
                SummaryText.Text = "已取消导入，未写入批次数据。";
                return;
            }

            var batchId = await RunQueuedAsync(sender, BackgroundTaskKind.Import, "正在提交导入数据...", context =>
                service.CommitImportAsync(preview, App.Settings.OperatorName, context.CancellationToken));

            if (SelectedTemplate?.Id == templateId)
            {
                _batchCurrentPage = 1;
                await LoadBatchesAsync();

                var batches = BatchGrid.ItemsSource?.Cast<LabelImportBatch>().ToList() ?? new List<LabelImportBatch>();
                BatchGrid.SelectedItem = batches.FirstOrDefault(x => x.Id == batchId);
            }

            AppMessageBox.Show("Excel 数据已导入数据库。");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BatchGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async void VoidBatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LabelImportBatch batch })
            return;

        if (string.Equals(batch.Status, "Voided", StringComparison.OrdinalIgnoreCase))
        {
            AppMessageBox.Show("当前批次已经作废。");
            return;
        }

        if (AppMessageBox.Show($"确定作废批次 {batch.ExcelFileName}？作废后默认列表会隐藏该批次，但历史记录仍保留。", "确认作废", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            batch.Status = "Voided";
            await AppDb.Db.Updateable(batch).ExecuteCommandAsync();
            await LoadBatchesAsync();
            AppMessageBox.Show("批次已作废。");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"作废批次失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadRowsAsync()
    {
        var template = SelectedTemplate;
        var batch = SelectedBatch;
        if (template == null || batch == null)
        {
            ClearRows();
            return;
        }

        var onlyInvalid = OnlyInvalidBox.IsChecked == true;
        var onlyUnprinted = OnlyUnprintedBox.IsChecked == true;
        var rowKeyword = RowSearchBox?.Text.Trim() ?? string.Empty;
        var token = ResetCancellation(ref _rowLoadCts);

        try
        {
            RowLoadingOverlay.Visibility = Visibility.Visible;
            var fields = await AppDb.Db.Queryable<LabelTemplateField>()
                .Where(x => x.TemplateId == template.Id && !x.IsDeleted)
                .OrderBy(x => x.Sort)
                .ToListAsync();

            var rowQuery = AppDb.Db.Queryable<LabelImportRow>()
                .Where(x => x.BatchId == batch.Id);
            if (onlyInvalid)
                rowQuery = rowQuery.Where(x => !x.IsValid);
            if (onlyUnprinted)
                rowQuery = rowQuery.Where(x => !x.IsPrinted);
            if (!string.IsNullOrWhiteSpace(rowKeyword))
            {
                if (int.TryParse(rowKeyword, out var rowIndex))
                    rowQuery = rowQuery.Where(x => x.RowIndex == rowIndex || x.RowDataJson.Contains(rowKeyword) || (x.ErrorMessage != null && x.ErrorMessage.Contains(rowKeyword)));
                else
                    rowQuery = rowQuery.Where(x => x.RowDataJson.Contains(rowKeyword) || (x.ErrorMessage != null && x.ErrorMessage.Contains(rowKeyword)));
            }

            RefAsync<int> totalRowsRef = 0;
            var currentPage = Math.Max(1, _rowCurrentPage);
            var dbRows = await rowQuery
                .OrderBy(x => x.RowIndex)
                .ToPageListAsync(currentPage, _rowPageSize, totalRowsRef);
            var totalRows = totalRowsRef.Value;
            var totalPages = Math.Max(1, (totalRows + _rowPageSize - 1) / _rowPageSize);

            if (currentPage > totalPages)
            {
                currentPage = totalPages;
                totalRowsRef = 0;
                dbRows = await rowQuery
                    .OrderBy(x => x.RowIndex)
                    .ToPageListAsync(currentPage, _rowPageSize, totalRowsRef);
                totalRows = totalRowsRef.Value;
                totalPages = Math.Max(1, (totalRows + _rowPageSize - 1) / _rowPageSize);
            }

            if (token.IsCancellationRequested ||
                SelectedTemplate?.Id != template.Id ||
                SelectedBatch?.Id != batch.Id)
            {
                return;
            }

            _rowTotalRows = totalRows;
            _rowTotalPages = totalPages;
            _rowCurrentPage = currentPage;

            var pageRows = await Task.Run(() => dbRows.Select(x => new ImportRowGridItem
            {
                Id = x.Id,
                RowIndex = x.RowIndex,
                IsValid = x.IsValid,
                IsPrinted = x.IsPrinted,
                PrintCount = x.PrintCount,
                ErrorMessage = x.ErrorMessage,
                IsSelected = false,
                Data = GridRowDataHelper.Deserialize(x.RowDataJson, fields)
            }).ToList(), token);

            BuildRowGridColumnsIfNeeded(fields);
            ReplaceRows(pageRows);
            RowGrid.ItemsSource = _rows;
            UpdateRowEmptyState(batch);
            UpdateSummary();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"加载明细失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RowLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildRowGridColumns(IReadOnlyList<LabelTemplateField> fields)
    {
        RowGrid.Columns.Clear();
        var centerCellStyle = FindAppStyle("AppDataGridCenterCellStyle");
        var centerHeaderStyle = FindAppStyle("AppDataGridCenterColumnHeaderStyle");

        RowGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "操作",
            Width = DataGridLength.Auto,
            CellTemplate = BuildRowActionTemplate()
        });
        RowGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "选择",
            CellTemplate = BuildBooleanCheckBoxTemplate(nameof(ImportRowGridItem.IsSelected), true),
            CellStyle = centerCellStyle,
            HeaderStyle = centerHeaderStyle,
            Width = 70
        });
        RowGrid.Columns.Add(new DataGridTextColumn { Header = "Excel行号", Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.RowIndex)), Width = 100, IsReadOnly = true });
        RowGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "是否有效",
            CellTemplate = BuildBooleanCheckBoxTemplate(nameof(ImportRowGridItem.IsValid), false),
            CellStyle = centerCellStyle,
            HeaderStyle = centerHeaderStyle,
            Width = 90,
            IsReadOnly = true
        });
        RowGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "已打印",
            CellTemplate = BuildBooleanCheckBoxTemplate(nameof(ImportRowGridItem.IsPrinted), false),
            CellStyle = centerCellStyle,
            HeaderStyle = centerHeaderStyle,
            Width = 90,
            IsReadOnly = true
        });
        RowGrid.Columns.Add(new DataGridTextColumn { Header = "打印次数", Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.PrintCount)), Width = 90, IsReadOnly = true });

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

        RowGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "错误信息",
            Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.ErrorMessage)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true
        });
    }

    private static DataTemplate BuildBooleanCheckBoxTemplate(string bindingPath, bool isInteractive)
    {
        var container = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
        container.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        container.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Stretch);

        var checkBox = new FrameworkElementFactory(typeof(System.Windows.Controls.CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(bindingPath)
        {
            Mode = isInteractive ? BindingMode.TwoWay : BindingMode.OneWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

        if (!isInteractive)
        {
            checkBox.SetValue(UIElement.FocusableProperty, false);
            checkBox.SetValue(UIElement.IsHitTestVisibleProperty, false);
        }

        container.AppendChild(checkBox);

        return new DataTemplate
        {
            VisualTree = container
        };
    }

    private static Style FindAppStyle(string resourceKey)
    {
        return (Style)System.Windows.Application.Current.FindResource(resourceKey);
    }

    private void BuildRowGridColumnsIfNeeded(IReadOnlyList<LabelTemplateField> fields)
    {
        var signature = BuildFieldSignature(fields);
        if (string.Equals(_rowGridColumnSignature, signature, StringComparison.Ordinal))
            return;

        _rowGridColumnSignature = signature;
        BuildRowGridColumns(fields);
    }

    private static string BuildFieldSignature(IEnumerable<LabelTemplateField> fields)
    {
        return string.Join("|", fields.Select(x => $"{x.Id}:{x.Sort}:{x.FieldCode}:{x.FieldName}"));
    }

    private DataTemplate BuildRowActionTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);
        panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);

        var previewButton = BuildRowActionButton("预览", MahApps.Metro.IconPacks.PackIconMaterialKind.Eye, PreviewRow_Click, false);
        previewButton.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        panel.AppendChild(previewButton);

        panel.AppendChild(BuildRowActionButton("打印", MahApps.Metro.IconPacks.PackIconMaterialKind.Printer, PrintRow_Click, true));

        return new DataTemplate
        {
            VisualTree = panel
        };
    }

    private static FrameworkElementFactory BuildRowActionButton(string content, MahApps.Metro.IconPacks.PackIconMaterialKind iconKind, System.Windows.RoutedEventHandler clickHandler, bool isPrimary)
    {
        var button = new FrameworkElementFactory(typeof(System.Windows.Controls.Button));
        button.SetValue(System.Windows.FrameworkElement.HeightProperty, 28.0);
        button.SetValue(System.Windows.FrameworkElement.StyleProperty, System.Windows.Application.Current.FindResource(isPrimary ? "ButtonPrimary" : "AppSecondaryButtonStyle"));
        button.SetBinding(System.Windows.UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(ImportRowGridItem.IsValid)));
        button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, clickHandler);

        var panel = BuildButtonContent(content, iconKind);
        button.AppendChild(panel);
        return button;
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

    private void ClearRows()
    {
        ClearRowItems();
        _rowCurrentPage = 1;
        _rowTotalRows = 0;
        _rowTotalPages = 1;
        RowGrid.ItemsSource = _rows;
        UpdateRowEmptyState(null);
        UpdateSummary();
    }

    private void ReplaceRows(IEnumerable<ImportRowGridItem> rows)
    {
        ClearRowItems();
        foreach (var item in rows)
        {
            item.PropertyChanged += Row_PropertyChanged;
            _rows.Add(item);
        }
    }

    private void ClearRowItems()
    {
        foreach (var item in _rows)
            item.PropertyChanged -= Row_PropertyChanged;
        _rows.Clear();
    }

    private void ClearBatches()
    {
        if (BatchGrid != null)
            BatchGrid.ItemsSource = null;

        _batchCurrentPage = 1;
        _batchTotalRows = 0;
        _batchTotalPages = 1;
        UpdateBatchPagination();
        UpdateBatchEmptyState();
    }

    private async Task ApplyFilterAsync()
    {
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async void Filter_Changed(object sender, RoutedEventArgs e) => await ApplyFilterAsync();

    private string GetSelectedBatchStatusFilter()
    {
        if (BatchStatusFilterBox?.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "Active";

        return "Active";
    }

    private void UpdateBatchEmptyState()
    {
        if (BatchEmptyText == null)
            return;

        if (SelectedTemplate == null)
            BatchEmptyText.Text = "请选择模板查看导入批次";
        else if (_batchTotalRows == 0)
            BatchEmptyText.Text = "暂无符合条件的导入批次";
        else
            BatchEmptyText.Text = string.Empty;

        BatchEmptyText.Visibility = _batchTotalRows == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRowEmptyState(LabelImportBatch? batch)
    {
        if (RowEmptyText == null)
            return;

        if (SelectedTemplate == null)
            RowEmptyText.Text = "请选择模板";
        else if (batch == null)
            RowEmptyText.Text = "请选择批次查看明细";
        else if (_rowTotalRows == 0)
            RowEmptyText.Text = "暂无符合条件的明细数据";
        else
            RowEmptyText.Text = string.Empty;

        RowEmptyText.Visibility = _rowTotalRows == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Row_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportRowGridItem.IsSelected) && !_isBulkSelectingRows)
            UpdateSummary();
    }

    private async void FirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage = 1;
        await LoadRowsAsync();
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage--;
        await LoadRowsAsync();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage++;
        await LoadRowsAsync();
    }

    private async void LastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage = _rowTotalPages;
        await LoadRowsAsync();
    }

    private async void PageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowPageSize = GetSelectedRowPageSize();
        _rowCurrentPage = 1;
        if (SelectedBatch != null)
            await LoadRowsAsync();
        else
            UpdateSummary();
    }

    private async void BatchFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage <= 1) return;
        _batchCurrentPage = 1;
        await LoadBatchesAsync();
    }

    private async void BatchPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage <= 1) return;
        _batchCurrentPage--;
        await LoadBatchesAsync();
    }

    private async void BatchNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage >= _batchTotalPages) return;
        _batchCurrentPage++;
        await LoadBatchesAsync();
    }

    private async void BatchLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage >= _batchTotalPages) return;
        _batchCurrentPage = _batchTotalPages;
        await LoadBatchesAsync();
    }

    private async void BatchPageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _batchPageSize = GetSelectedBatchPageSize();
        _batchCurrentPage = 1;
        if (SelectedTemplate != null)
            await LoadBatchesAsync();
        else
            UpdateBatchPagination();
    }

    private void SelectValidUnprinted_Click(object sender, RoutedEventArgs e)
    {
        ApplyRowSelection(row => row.IsValid && !row.IsPrinted);
    }

    private void SelectValid_Click(object sender, RoutedEventArgs e)
    {
        ApplyRowSelection(row => row.IsValid);
    }

    private void ReverseSelect_Click(object sender, RoutedEventArgs e)
    {
        ApplyRowSelection(row => !row.IsSelected);
    }

    private void ClearSelect_Click(object sender, RoutedEventArgs e)
    {
        ApplyRowSelection(_ => false);
    }

    private void ApplyRowSelection(Func<ImportRowGridItem, bool> selector)
    {
        _isBulkSelectingRows = true;
        try
        {
            foreach (var row in _rows)
                row.IsSelected = selector(row);
        }
        finally
        {
            _isBulkSelectingRows = false;
        }

        UpdateSummary();
    }

    private async void PreviewRow_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRowActionContext(sender, out var template, out var batch, out var row))
            return;

        if (!TryGetPrintCopies(out var printCopies))
            return;

        try
        {
            await RunQueuedAsync(sender, BackgroundTaskKind.Preview, "正在打开标签预览...", context =>
                new LabelPrintService(App.Settings).PreviewSelectedRowsAsync(template.Id, batch.Id, new[] { row.Id }, printCopies, context.CancellationToken));
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"预览失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PrintRow_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRowActionContext(sender, out var template, out var batch, out var row))
            return;

        if (!TryGetPrintCopies(out var printCopies))
            return;

        if (!TryGetPrinterName(out var printerName))
            return;

        if (!EnsureBatchCanPrint(batch))
            return;

        if (App.Settings.ConfirmBeforePrint &&
            AppMessageBox.Show($"确定打印 Excel 第 {row.RowIndex} 行数据，{printCopies} 张？", "确认打印", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        try
        {
            await RunQueuedAsync(sender, BackgroundTaskKind.Print, "正在打印当前行...", context =>
                new LabelPrintService(App.Settings).PrintSelectedRowsAsync(
                    template.Id,
                    batch.Id,
                    new[] { row.Id },
                    printerName,
                    printCopies,
                    context.CancellationToken,
                    context.Progress));
            AppMessageBox.Show("打印任务已完成。");
            await LoadRowsAsync();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"打印失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PrintSelected_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        var batch = SelectedBatch;
        if (template == null || batch == null)
        {
            AppMessageBox.Show("请先选择模板和导入批次。");
            return;
        }

        var selectedIds = GetSelectedValidRowIds();
        if (selectedIds.Count == 0)
        {
            AppMessageBox.Show("请选择有效数据行。");
            return;
        }

        if (!TryGetPrintCopies(out var printCopies))
            return;

        if (!TryGetPrinterName(out var printerName))
            return;

        if (!EnsureBatchCanPrint(batch))
            return;

        var totalLabels = selectedIds.Count * printCopies;
        if (App.Settings.ConfirmBeforePrint &&
            AppMessageBox.Show($"确定批量打印选中的 {selectedIds.Count} 行数据，每行 {printCopies} 张，共 {totalLabels} 张？", "确认批量打印", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        try
        {
            await RunQueuedAsync(sender, BackgroundTaskKind.Print, "正在批量打印...", context =>
                new LabelPrintService(App.Settings).PrintSelectedRowsAsync(
                    template.Id,
                    batch.Id,
                    selectedIds,
                    printerName,
                    printCopies,
                    context.CancellationToken,
                    context.Progress));
            AppMessageBox.Show("打印任务已完成。");
            await LoadRowsAsync();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"打印失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PreviewSelected_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        var batch = SelectedBatch;
        if (template == null || batch == null)
        {
            AppMessageBox.Show("请先选择模板和导入批次。");
            return;
        }

        var selectedIds = GetSelectedValidRowIds();
        if (selectedIds.Count == 0)
        {
            AppMessageBox.Show("请选择有效数据行。");
            return;
        }

        if (!TryGetPrintCopies(out var printCopies))
            return;

        try
        {
            await RunQueuedAsync(sender, BackgroundTaskKind.Preview, "正在生成批量标签预览...", context =>
                new LabelPrintService(App.Settings).PreviewSelectedRowsAsync(
                    template.Id,
                    batch.Id,
                    selectedIds,
                    printCopies,
                    context.CancellationToken));
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"预览失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryGetRowActionContext(object sender, out LabelTemplate template, out LabelImportBatch batch, out ImportRowGridItem row)
    {
        template = null!;
        batch = null!;
        row = null!;

        if (sender is not FrameworkElement { DataContext: ImportRowGridItem rowItem })
            return false;

        if (!rowItem.IsValid)
        {
            AppMessageBox.Show("当前行是无效数据，不能预览或打印。");
            return false;
        }

        var selectedTemplate = SelectedTemplate;
        var selectedBatch = SelectedBatch;
        if (selectedTemplate == null || selectedBatch == null)
        {
            AppMessageBox.Show("请先选择模板和导入批次。");
            return false;
        }

        template = selectedTemplate;
        batch = selectedBatch;
        row = rowItem;
        return true;
    }

    private bool TryGetPrintCopies(out int printCopies)
    {
        printCopies = 1;
        var text = PrintCopiesBox.Text.Trim();
        if (!int.TryParse(text, out var copies) || copies < 1 || copies > MaxPrintCopies)
        {
            AppMessageBox.Show($"打印份数必须是 1 到 {MaxPrintCopies} 之间的整数。");
            return false;
        }

        printCopies = copies;
        return true;
    }

    private bool TryGetPrinterName(out string printerName)
    {
        printerName = (PrinterNameBox.SelectedItem as string)?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(printerName))
            return true;

        AppMessageBox.Show("请先选择打印机。");
        return false;
    }

    private static bool EnsureBatchCanPrint(LabelImportBatch batch)
    {
        if (!string.Equals(batch.Status, "Voided", StringComparison.OrdinalIgnoreCase))
            return true;

        AppMessageBox.Show("当前批次已作废，不能从打印中心继续打印。可以在打印记录中按历史任务重打印。", "批次已作废", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private List<long> GetSelectedValidRowIds()
    {
        return _rows
            .Where(x => x.IsSelected && x.IsValid)
            .Select(x => x.Id)
            .ToList();
    }

    private async Task RunQueuedAsync(object sender, string runningText, Func<CancellationToken, Task> operation)
    {
        await RunQueuedAsync(
            sender,
            BackgroundTaskKind.Other,
            runningText,
            context => operation(context.CancellationToken));
    }

    private async Task<T> RunQueuedAsync<T>(object sender, string runningText, Func<CancellationToken, Task<T>> operation)
    {
        return await RunQueuedAsync(
            sender,
            BackgroundTaskKind.Other,
            runningText,
            context => operation(context.CancellationToken));
    }

    private async Task RunQueuedAsync(object sender, BackgroundTaskKind kind, string runningText, Func<BackgroundTaskContext, Task> operation)
    {
        await RunQueuedAsync<object?>(
            sender,
            kind,
            runningText,
            async context =>
            {
                await operation(context);
                return null;
            });
    }

    private async Task<T> RunQueuedAsync<T>(object sender, BackgroundTaskKind kind, string runningText, Func<BackgroundTaskContext, Task<T>> operation)
    {
        var element = GetTemporarilyDisabledElement(sender);
        if (element != null)
            element.IsEnabled = false;

        if (SummaryText != null)
            SummaryText.Text = runningText;

        try
        {
            return await BackgroundTaskQueue.Shared.EnqueueAsync(kind, runningText, operation);
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
            UpdateSummary();
        }
    }

    private static UIElement? GetTemporarilyDisabledElement(object sender)
    {
        if (sender is not UIElement element)
            return null;

        return BindingOperations.GetBindingExpression(element, UIElement.IsEnabledProperty) == null
            ? element
            : null;
    }

    private void UpdateSummary()
    {
        if (SummaryText == null || PageInfoText == null ||
            FirstPageButton == null || PrevPageButton == null ||
            NextPageButton == null || LastPageButton == null)
        {
            return;
        }

        var selected = _rows.Count(x => x.IsSelected && x.IsValid);
        SummaryText.Text = $"当前页 {_rows.Count} 行 / 共 {_rowTotalRows} 行，已选有效行 {selected} 行。可单行预览/打印，也可批量打印。";
        PageInfoText.Text = $"{_rowCurrentPage} / {_rowTotalPages}";

        var hasRows = _rowTotalRows > 0;
        FirstPageButton.IsEnabled = hasRows && _rowCurrentPage > 1;
        PrevPageButton.IsEnabled = hasRows && _rowCurrentPage > 1;
        NextPageButton.IsEnabled = hasRows && _rowCurrentPage < _rowTotalPages;
        LastPageButton.IsEnabled = hasRows && _rowCurrentPage < _rowTotalPages;
    }

    private void UpdateBatchPagination()
    {
        if (BatchPageInfoText == null ||
            BatchFirstPageButton == null || BatchPrevPageButton == null ||
            BatchNextPageButton == null || BatchLastPageButton == null)
        {
            return;
        }

        BatchPageInfoText.Text = $"{_batchCurrentPage} / {_batchTotalPages}，共 {_batchTotalRows} 批";

        var hasRows = _batchTotalRows > 0;
        BatchFirstPageButton.IsEnabled = hasRows && _batchCurrentPage > 1;
        BatchPrevPageButton.IsEnabled = hasRows && _batchCurrentPage > 1;
        BatchNextPageButton.IsEnabled = hasRows && _batchCurrentPage < _batchTotalPages;
        BatchLastPageButton.IsEnabled = hasRows && _batchCurrentPage < _batchTotalPages;
    }

    private int GetSelectedRowPageSize()
    {
        if (PageSizeBox?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var pageSize) &&
            pageSize > 0)
        {
            return pageSize;
        }

        return DefaultRowPageSize;
    }

    private int GetSelectedBatchPageSize()
    {
        if (BatchPageSizeBox?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var pageSize) &&
            pageSize > 0)
        {
            return pageSize;
        }

        return DefaultBatchPageSize;
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
        CancelAndDispose(ref _refreshCts);
        CancelAndDispose(ref _templateLoadCts);
        CancelAndDispose(ref _batchLoadCts);
        CancelAndDispose(ref _rowLoadCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}


