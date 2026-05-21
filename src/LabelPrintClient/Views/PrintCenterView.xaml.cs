using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using LabelPrintClient.Services.Excel;
using LabelPrintClient.Services.Import;
using LabelPrintClient.Services.Print;
using LabelPrintClient.ViewModels;
using Microsoft.Win32;

namespace LabelPrintClient.Views;

public partial class PrintCenterView : System.Windows.Controls.UserControl
{
    private const int DefaultBatchPageSize = 20;
    private const int DefaultRowPageSize = 50;
    private const int MaxPrintCopies = 999;

    private readonly ObservableCollection<ImportRowGridItem> _rows = new();
    private int _batchCurrentPage = 1;
    private int _batchPageSize = DefaultBatchPageSize;
    private int _batchTotalRows;
    private int _batchTotalPages = 1;
    private int _rowCurrentPage = 1;
    private int _rowPageSize = DefaultRowPageSize;
    private int _rowTotalRows;
    private int _rowTotalPages = 1;

    public PrintCenterView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadPrinters();
            RefreshAll();
        };
    }

    private LabelCategory? SelectedCategory => CategoryBox.SelectedItem as LabelCategory;
    private LabelTemplate? SelectedTemplate => TemplateBox.SelectedItem as LabelTemplate;
    private LabelImportBatch? SelectedBatch => BatchGrid.SelectedItem as LabelImportBatch;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void LoadPrinters()
    {
        var printerNames = PrinterSettings.InstalledPrinters
            .Cast<string>()
            .OrderBy(x => x)
            .ToList();

        PrinterNameBox.ItemsSource = printerNames;

        var defaultPrinter = new PrinterSettings().PrinterName;
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

    private void RefreshAll()
    {
        CategoryBox.ItemsSource = AppDb.Db.Queryable<LabelCategory>()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Sort)
            .ToList();
        TemplateBox.ItemsSource = null;
        ClearBatches();
        ClearRows();
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedCategory == null) return;
        TemplateBox.ItemsSource = AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.CategoryId == SelectedCategory.Id && x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToList();
        ClearBatches();
        ClearRows();
    }

    private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _batchCurrentPage = 1;
        LoadBatches();
    }

    private void LoadBatches_Click(object sender, RoutedEventArgs e) => LoadBatches();

    private void LoadBatches()
    {
        if (SelectedTemplate == null)
        {
            ClearBatches();
            ClearRows();
            return;
        }

        var batchQuery = AppDb.Db.Queryable<LabelImportBatch>()
            .Where(x => x.TemplateId == SelectedTemplate.Id);

        _batchTotalRows = batchQuery.Count();
        _batchTotalPages = Math.Max(1, (int)Math.Ceiling(_batchTotalRows / (double)_batchPageSize));
        if (_batchCurrentPage > _batchTotalPages) _batchCurrentPage = _batchTotalPages;
        if (_batchCurrentPage < 1) _batchCurrentPage = 1;

        BatchGrid.ItemsSource = batchQuery
            .OrderByDescending(x => x.ImportTime)
            .OrderByDescending(x => x.Id)
            .Skip((_batchCurrentPage - 1) * _batchPageSize)
            .Take(_batchPageSize)
            .ToList();
        ClearRows();
        UpdateBatchPagination();
    }

    private void DownloadExcel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate == null)
        {
            System.Windows.MessageBox.Show("请先选择模板。");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            FileName = $"{SelectedTemplate.Name}_导入模板.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        new ExcelTemplateExportService().Export(SelectedTemplate.Id, dialog.FileName);
        System.Windows.MessageBox.Show("Excel 模板已生成。");
    }

    private void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate == null)
        {
            System.Windows.MessageBox.Show("请先选择模板。");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel 文件|*.xlsx;*.xlsm|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var service = new LabelImportService();
            var batchId = service.ImportExcel(SelectedTemplate.Id, dialog.FileName, App.Settings.OperatorName);
            _batchCurrentPage = 1;
            LoadBatches();

            var batches = BatchGrid.ItemsSource?.Cast<LabelImportBatch>().ToList() ?? new List<LabelImportBatch>();
            BatchGrid.SelectedItem = batches.FirstOrDefault(x => x.Id == batchId);
            System.Windows.MessageBox.Show("Excel 数据已导入数据库。");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BatchGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowCurrentPage = 1;
        LoadRows();
    }

    private void LoadRows()
    {
        var template = SelectedTemplate;
        var batch = SelectedBatch;
        if (template == null || batch == null)
        {
            ClearRows();
            return;
        }

        var fields = AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .OrderBy(x => x.Sort)
            .ToList();

        var rowQuery = AppDb.Db.Queryable<LabelImportRow>()
            .Where(x => x.BatchId == batch.Id);
        if (OnlyInvalidBox.IsChecked == true)
            rowQuery = rowQuery.Where(x => !x.IsValid);
        if (OnlyUnprintedBox.IsChecked == true)
            rowQuery = rowQuery.Where(x => !x.IsPrinted);

        _rowTotalRows = rowQuery.Count();
        _rowTotalPages = Math.Max(1, (int)Math.Ceiling(_rowTotalRows / (double)_rowPageSize));
        if (_rowCurrentPage > _rowTotalPages) _rowCurrentPage = _rowTotalPages;
        if (_rowCurrentPage < 1) _rowCurrentPage = 1;

        var dbRows = rowQuery
            .OrderBy(x => x.RowIndex)
            .Skip((_rowCurrentPage - 1) * _rowPageSize)
            .Take(_rowPageSize)
            .ToList();

        var pageRows = dbRows.Select(x => new ImportRowGridItem
        {
            Id = x.Id,
            RowIndex = x.RowIndex,
            IsValid = x.IsValid,
            IsPrinted = x.IsPrinted,
            PrintCount = x.PrintCount,
            ErrorMessage = x.ErrorMessage,
            IsSelected = false,
            Data = JsonHelper.Deserialize<Dictionary<string, string>>(x.RowDataJson) ?? new Dictionary<string, string>()
        }).ToList();

        BuildRowGridColumns(fields);
        _rows.Clear();
        foreach (var item in pageRows)
        {
            item.PropertyChanged += Row_PropertyChanged;
            _rows.Add(item);
        }
        RowGrid.ItemsSource = _rows;
        UpdateSummary();
    }

    private void BuildRowGridColumns(IReadOnlyList<LabelTemplateField> fields)
    {
        RowGrid.Columns.Clear();

        RowGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "操作",
            Width = 124,
            CellTemplate = BuildRowActionTemplate()
        });
        RowGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "选择",
            Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.IsSelected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 70
        });
        RowGrid.Columns.Add(new DataGridTextColumn { Header = "Excel行", Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.RowIndex)), Width = 80, IsReadOnly = true });
        RowGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "有效", Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.IsValid)), Width = 70, IsReadOnly = true });
        RowGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "已打印", Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.IsPrinted)), Width = 80, IsReadOnly = true });
        RowGrid.Columns.Add(new DataGridTextColumn { Header = "打印次数", Binding = new System.Windows.Data.Binding(nameof(ImportRowGridItem.PrintCount)), Width = 80, IsReadOnly = true });

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

    private DataTemplate BuildRowActionTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);

        var previewButton = BuildRowActionButton("预览", PreviewRow_Click, false);
        previewButton.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        panel.AppendChild(previewButton);

        panel.AppendChild(BuildRowActionButton("打印", PrintRow_Click, true));

        return new DataTemplate
        {
            VisualTree = panel
        };
    }

    private static FrameworkElementFactory BuildRowActionButton(string content, RoutedEventHandler clickHandler, bool isPrimary)
    {
        var button = new FrameworkElementFactory(typeof(Wpf.Ui.Controls.Button));
        button.SetValue(ContentControl.ContentProperty, content);
        button.SetValue(FrameworkElement.WidthProperty, 52.0);
        button.SetValue(FrameworkElement.HeightProperty, 28.0);
        button.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(ImportRowGridItem.IsValid)));
        button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, clickHandler);
        if (isPrimary)
            button.SetValue(Wpf.Ui.Controls.Button.AppearanceProperty, Wpf.Ui.Controls.ControlAppearance.Primary);
        return button;
    }

    private void ClearRows()
    {
        _rows.Clear();
        _rowCurrentPage = 1;
        _rowTotalRows = 0;
        _rowTotalPages = 1;
        RowGrid.ItemsSource = _rows;
        UpdateSummary();
    }

    private void ClearBatches()
    {
        if (BatchGrid != null)
            BatchGrid.ItemsSource = null;

        _batchCurrentPage = 1;
        _batchTotalRows = 0;
        _batchTotalPages = 1;
        UpdateBatchPagination();
    }

    private void ApplyFilter()
    {
        _rowCurrentPage = 1;
        LoadRows();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void Row_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportRowGridItem.IsSelected))
            UpdateSummary();
    }

    private void FirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage = 1;
        LoadRows();
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage <= 1) return;
        _rowCurrentPage--;
        LoadRows();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage++;
        LoadRows();
    }

    private void LastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCurrentPage >= _rowTotalPages) return;
        _rowCurrentPage = _rowTotalPages;
        LoadRows();
    }

    private void PageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _rowPageSize = GetSelectedRowPageSize();
        _rowCurrentPage = 1;
        if (SelectedBatch != null)
            LoadRows();
        else
            UpdateSummary();
    }

    private void BatchFirstPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage <= 1) return;
        _batchCurrentPage = 1;
        LoadBatches();
    }

    private void BatchPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage <= 1) return;
        _batchCurrentPage--;
        LoadBatches();
    }

    private void BatchNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage >= _batchTotalPages) return;
        _batchCurrentPage++;
        LoadBatches();
    }

    private void BatchLastPage_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCurrentPage >= _batchTotalPages) return;
        _batchCurrentPage = _batchTotalPages;
        LoadBatches();
    }

    private void BatchPageSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _batchPageSize = GetSelectedBatchPageSize();
        _batchCurrentPage = 1;
        if (SelectedTemplate != null)
            LoadBatches();
        else
            UpdateBatchPagination();
    }

    private void SelectValidUnprinted_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = row.IsValid && !row.IsPrinted;
        RowGrid.Items.Refresh();
        UpdateSummary();
    }

    private void SelectValid_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = row.IsValid;
        RowGrid.Items.Refresh();
        UpdateSummary();
    }

    private void ReverseSelect_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = !row.IsSelected;
        RowGrid.Items.Refresh();
        UpdateSummary();
    }

    private void ClearSelect_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = false;
        RowGrid.Items.Refresh();
        UpdateSummary();
    }

    private void PreviewRow_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRowActionContext(sender, out var template, out var batch, out var row))
        {
            return;
        }

        if (!TryGetPrintCopies(out var printCopies))
            return;

        try
        {
            new LabelPrintService(App.Settings).PreviewSelectedRows(template.Id, batch.Id, new[] { row.Id }, printCopies);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"预览失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PrintRow_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRowActionContext(sender, out var template, out var batch, out var row))
        {
            return;
        }

        if (!TryGetPrintCopies(out var printCopies))
            return;

        if (!TryGetPrinterName(out var printerName))
            return;

        if (System.Windows.MessageBox.Show($"确定打印 Excel 第 {row.RowIndex} 行数据，{printCopies} 张？", "确认打印", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        try
        {
            new LabelPrintService(App.Settings).PrintSelectedRows(template.Id, batch.Id, new[] { row.Id }, printerName, printCopies);
            System.Windows.MessageBox.Show("打印任务已完成。");
            LoadRows();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"打印失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PreviewSelected_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        var batch = SelectedBatch;
        if (template == null || batch == null)
        {
            System.Windows.MessageBox.Show("请先选择模板和导入批次。");
            return;
        }

        var selectedIds = GetSelectedValidRowIds();
        if (selectedIds.Count == 0)
        {
            System.Windows.MessageBox.Show("请选择有效数据行。");
            return;
        }

        if (!TryGetPrintCopies(out var printCopies))
            return;

        try
        {
            new LabelPrintService(App.Settings).PreviewSelectedRows(template.Id, batch.Id, selectedIds, printCopies);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"预览失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PrintSelected_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        var batch = SelectedBatch;
        if (template == null || batch == null)
        {
            System.Windows.MessageBox.Show("请先选择模板和导入批次。");
            return;
        }

        var selectedIds = GetSelectedValidRowIds();
        if (selectedIds.Count == 0)
        {
            System.Windows.MessageBox.Show("请选择有效数据行。");
            return;
        }

        if (!TryGetPrintCopies(out var printCopies))
            return;

        if (!TryGetPrinterName(out var printerName))
            return;

        var totalLabels = selectedIds.Count * printCopies;
        if (System.Windows.MessageBox.Show($"确定批量打印选中的 {selectedIds.Count} 行数据，每行 {printCopies} 张，共 {totalLabels} 张？", "确认批量打印", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        try
        {
            new LabelPrintService(App.Settings).PrintSelectedRows(template.Id, batch.Id, selectedIds, printerName, printCopies);
            System.Windows.MessageBox.Show("打印任务已完成。");
            LoadRows();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"打印失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            System.Windows.MessageBox.Show("当前行是无效数据，不能预览或打印。");
            return false;
        }

        var selectedTemplate = SelectedTemplate;
        var selectedBatch = SelectedBatch;
        if (selectedTemplate == null || selectedBatch == null)
        {
            System.Windows.MessageBox.Show("请先选择模板和导入批次。");
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
            System.Windows.MessageBox.Show($"打印份数必须是 1 到 {MaxPrintCopies} 之间的整数。");
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

        System.Windows.MessageBox.Show("请先选择打印机。");
        return false;
    }

    private List<long> GetSelectedValidRowIds()
    {
        return _rows
            .Where(x => x.IsSelected && x.IsValid)
            .Select(x => x.Id)
            .ToList();
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
        SummaryText.Text = $"当前页 {_rows.Count} 行 / 共 {_rowTotalRows} 行，已选有效行 {selected} 行。可单行操作，也可批量预览/打印。";
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
}
