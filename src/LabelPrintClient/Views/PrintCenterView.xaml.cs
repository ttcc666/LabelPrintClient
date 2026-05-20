using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<ImportRowGridItem> _rows = new();
    private List<ImportRowGridItem> _allRows = new();

    public PrintCenterView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAll();
    }

    private LabelCategory? SelectedCategory => CategoryBox.SelectedItem as LabelCategory;
    private LabelTemplate? SelectedTemplate => TemplateBox.SelectedItem as LabelTemplate;
    private LabelImportBatch? SelectedBatch => BatchGrid.SelectedItem as LabelImportBatch;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        CategoryBox.ItemsSource = AppDb.Db.Queryable<LabelCategory>()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Sort)
            .ToList();
        TemplateBox.ItemsSource = null;
        BatchGrid.ItemsSource = null;
        ClearRows();
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedCategory == null) return;
        TemplateBox.ItemsSource = AppDb.Db.Queryable<LabelTemplate>()
            .Where(x => x.CategoryId == SelectedCategory.Id && x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToList();
        BatchGrid.ItemsSource = null;
        ClearRows();
    }

    private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadBatches();
    }

    private void LoadBatches_Click(object sender, RoutedEventArgs e) => LoadBatches();

    private void LoadBatches()
    {
        if (SelectedTemplate == null)
        {
            BatchGrid.ItemsSource = null;
            ClearRows();
            return;
        }

        BatchGrid.ItemsSource = AppDb.Db.Queryable<LabelImportBatch>()
            .Where(x => x.TemplateId == SelectedTemplate.Id)
            .OrderByDescending(x => x.ImportTime)
            .ToList();
        ClearRows();
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

        var dbRows = AppDb.Db.Queryable<LabelImportRow>()
            .Where(x => x.BatchId == batch.Id)
            .OrderBy(x => x.RowIndex)
            .ToList();

        _allRows = dbRows.Select(x => new ImportRowGridItem
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
        ApplyFilter();
    }

    private void BuildRowGridColumns(IReadOnlyList<LabelTemplateField> fields)
    {
        RowGrid.Columns.Clear();
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

    private void ClearRows()
    {
        _allRows.Clear();
        _rows.Clear();
        RowGrid.ItemsSource = _rows;
        SummaryText.Text = string.Empty;
    }

    private void ApplyFilter()
    {
        IEnumerable<ImportRowGridItem> query = _allRows;
        if (OnlyInvalidBox.IsChecked == true)
            query = query.Where(x => !x.IsValid);
        if (OnlyUnprintedBox.IsChecked == true)
            query = query.Where(x => !x.IsPrinted);

        _rows.Clear();
        foreach (var item in query)
        {
            _rows.Add(item);
        }
        RowGrid.ItemsSource = _rows;
        UpdateSummary();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

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
        try
        {
            new LabelPrintService(App.Settings).PreviewSelectedRows(template.Id, batch.Id, selectedIds);
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

        if (System.Windows.MessageBox.Show($"确定打印选中的 {selectedIds.Count} 行数据？", "确认打印", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        try
        {
            new LabelPrintService(App.Settings).PrintSelectedRows(template.Id, batch.Id, selectedIds, PrinterNameBox.Text.Trim());
            System.Windows.MessageBox.Show("打印任务已完成。");
            LoadRows();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"打印失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<long> GetSelectedValidRowIds()
    {
        return _rows.Where(x => x.IsSelected && x.IsValid).Select(x => x.Id).ToList();
    }

    private void UpdateSummary()
    {
        var selected = _rows.Count(x => x.IsSelected && x.IsValid);
        SummaryText.Text = $"当前显示 {_rows.Count} 行，已选有效行 {selected} 行。";
    }
}
