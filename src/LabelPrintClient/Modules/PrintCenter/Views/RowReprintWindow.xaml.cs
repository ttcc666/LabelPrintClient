using System.Windows;
using System.Drawing.Printing;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.ViewModels;
using LabelPrintClient.Services;
using SqlSugar;

namespace LabelPrintClient.Modules.PrintCenter.Views;

public partial class RowReprintWindow : Window
{
    private readonly LabelTemplate _template;
    private readonly LabelImportBatch _batch;
    private readonly ImportRowGridItem _rowItem;
    private List<string> _allSerials = new();
    private List<LabelPrintJobRow> _allJobRows = new();

    public List<LabelPrintJobRow> SelectedJobRows { get; private set; } = new();
    public string SelectedPrinterName { get; private set; } = string.Empty;
    public int PrintCopies { get; private set; } = 1;

    public RowReprintWindow(LabelTemplate template, LabelImportBatch batch, ImportRowGridItem rowItem)
    {
        InitializeComponent();
        _template = template;
        _batch = batch;
        _rowItem = rowItem;

        Loaded += async (_, _) =>
        {
            RenderProductSummary();
            LoadPrinters();
            await LoadSerialNumbersAsync();
        };
    }

    private void RenderProductSummary()
    {
        var summaryList = new List<string>();
        if (_rowItem.Data != null)
        {
            foreach (var kv in _rowItem.Data)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                {
                    summaryList.Add($"{kv.Key}: {kv.Value}");
                }
            }
        }

        ProductInfoText.Text = summaryList.Count > 0 
            ? string.Join(" | ", summaryList.Take(2)) 
            : AppLanguageService.GetString("Reprint.CurrentImportData");

        BatchInfoText.Text = AppLanguageService.Format("Reprint.BatchInfo", _batch.BatchNo, _batch.ExcelFileName);
    }

    private void LoadPrinters()
    {
        var printerNames = PrinterSettings.InstalledPrinters
            .Cast<string>()
            .OrderBy(x => x)
            .ToList();

        PrinterNameBox.ItemsSource = printerNames;
        
        // 预设默认打印机
        if (!string.IsNullOrWhiteSpace(App.Settings.DefaultPrinterName) &&
            printerNames.Contains(App.Settings.DefaultPrinterName))
        {
            PrinterNameBox.SelectedItem = App.Settings.DefaultPrinterName;
        }
        else
        {
            var defaultPrinter = new PrinterSettings().PrinterName;
            if (!string.IsNullOrWhiteSpace(defaultPrinter) && printerNames.Contains(defaultPrinter))
                PrinterNameBox.SelectedItem = defaultPrinter;
            else if (printerNames.Count > 0)
                PrinterNameBox.SelectedIndex = 0;
        }

        PrintCopiesBox.Text = Math.Clamp(App.Settings.DefaultPrintCopies, 1, 999).ToString();
    }

    private async Task LoadSerialNumbersAsync()
    {
        if (_template.TemplateMode != LabelTemplateMode.Serialized)
        {
            SerialPanel.Visibility = Visibility.Collapsed;
            CopiesPanel.Visibility = Visibility.Visible;
            return;
        }

        SerialPanel.Visibility = Visibility.Visible;
        CopiesPanel.Visibility = Visibility.Visible; // 序列号模板也支持每张打印多份的倍率

        StartSerialBox.IsEnabled = false;
        EndSerialBox.IsEnabled = false;

        try
        {
            // 查询本行产生过的所有 JobRows
            _allJobRows = await AppDb.Db.Queryable<LabelPrintJobRow>()
                .Where(x => x.ImportRowId == _rowItem.Id)
                .OrderBy(x => x.PrintJobId)
                .OrderBy(x => x.Id)
                .ToListAsync();

            var serials = new List<string>();
            var seenSerials = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in _allJobRows)
            {
                var sn = ExtractSerialNo(r);
                if (!string.IsNullOrWhiteSpace(sn) && seenSerials.Add(sn))
                {
                    serials.Add(sn);
                }
            }

            _allSerials = serials;

            if (_allSerials.Count == 0)
            {
                AppMessageBox.Show(AppLanguageService.GetString("Reprint.NoSerialHistory"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = false;
                Close();
                return;
            }

            StartSerialBox.ItemsSource = _allSerials;
            EndSerialBox.ItemsSource = _allSerials;

            StartSerialBox.SelectedIndex = 0;
            EndSerialBox.SelectedIndex = _allSerials.Count - 1;
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(AppLanguageService.Format("Reprint.LoadSerialFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StartSerialBox.IsEnabled = true;
            EndSerialBox.IsEnabled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var printerName = (PrinterNameBox.SelectedItem as string)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(printerName))
        {
            AppMessageBox.Show(AppLanguageService.GetString("Reprint.PrinterRequired"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PrintCopiesBox.Text.Trim(), out var copies) || copies < 1 || copies > 999)
        {
            AppMessageBox.Show(AppLanguageService.GetString("Reprint.CopiesRange999"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_template.TemplateMode == LabelTemplateMode.Serialized)
        {
            var startSerial = StartSerialBox.SelectedItem as string;
            var endSerial = EndSerialBox.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(startSerial) || string.IsNullOrWhiteSpace(endSerial))
            {
                AppMessageBox.Show(AppLanguageService.GetString("Reprint.SerialRangeRequired"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startIndex = _allSerials.IndexOf(startSerial);
            var endIndex = _allSerials.IndexOf(endSerial);

            if (startIndex < 0 || endIndex < 0)
            {
                AppMessageBox.Show(AppLanguageService.GetString("Reprint.SerialNotFound"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (endIndex < startIndex)
            {
                AppMessageBox.Show(AppLanguageService.GetString("Reprint.SerialEndBeforeStart"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targetSerials = _allSerials.Skip(startIndex).Take(endIndex - startIndex + 1).ToHashSet();
            var serialOrder = _allSerials
                .Select((SerialNo, Index) => new { SerialNo, Index })
                .ToDictionary(x => x.SerialNo, x => x.Index);
            SelectedJobRows = _allJobRows.Where(r =>
            {
                var sn = ExtractSerialNo(r);
                return !string.IsNullOrWhiteSpace(sn) && targetSerials.Contains(sn);
            }).OrderBy(r =>
            {
                var sn = ExtractSerialNo(r);
                return !string.IsNullOrWhiteSpace(sn) && serialOrder.TryGetValue(sn, out var index)
                    ? index
                    : int.MaxValue;
            }).ThenBy(r => r.PrintJobId)
            .ThenBy(r => r.Id)
            .ToList();

            if (SelectedJobRows.Count == 0)
            {
                AppMessageBox.Show(AppLanguageService.GetString("Reprint.NoMatchedRows"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        SelectedPrinterName = printerName;
        PrintCopies = copies;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string? ExtractSerialNo(LabelPrintJobRow row)
    {
        var dict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson);
        if (dict == null)
            return null;
        return dict.TryGetValue(TemplateSystemFields.SerialNo, out var sn) ? sn : null;
    }
}
