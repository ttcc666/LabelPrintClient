using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.PrintCenter.ViewModels;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;
using System.Windows;
using System.Windows.Controls;

namespace LabelPrintClient.Modules.PrintCenter.Views;

public partial class ImportPreviewWindow : Window
{
    private readonly ImportPreviewResult _preview;
    private readonly List<ImportPreviewRowGridItem> _rows;
    private CancellationTokenSource? _filterCts;

    public ImportPreviewWindow(ImportPreviewResult preview)
    {
        _preview = preview;
        _rows = preview.Rows
            .Select(x => new ImportPreviewRowGridItem
            {
                RowIndex = x.RowIndex,
                IsValid = x.IsValid,
                ErrorMessage = x.IsValid ? null : x.ErrorMessage,
                Data = GridRowDataHelper.Normalize(x.Data, preview.Fields)
            })
            .ToList();

        InitializeComponent();
        PreviewGrid.RowHeight = double.NaN;
        SummaryText.Text = $"{preview.ExcelFileName} · 共 {preview.TotalRows} 行，有效 {preview.ValidRows} 行，错误 {preview.InvalidRows} 行";
        ConfirmButton.IsEnabled = preview.InvalidRows == 0;
        ImportHintText.Text = preview.InvalidRows == 0
            ? "确认后才会写入导入批次和明细数据。"
            : "存在错误行，修正 Excel 后才能确认导入。";
        BuildColumns(preview.Fields);
        _ = ApplyFilterAsync();
    }

    private async void OnlyErrorRowsBox_Changed(object sender, RoutedEventArgs e)
    {
        await ApplyFilterAsync();
    }

    private async void ApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        await ApplyFilterAsync();
    }

    private async void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        PreviewSearchBox.Text = string.Empty;
        OnlyErrorRowsBox.IsChecked = false;
        await ApplyFilterAsync();
    }

    private async void PreviewSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        await ApplyFilterAsync();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_preview.InvalidRows > 0)
        {
            AppMessageBox.Show("存在错误行，不能确认导入。请修正 Excel 后重新导入。", "导入校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BuildColumns(IReadOnlyList<LabelTemplateField> fields)
    {
        PreviewGrid.Columns.Clear();
        var centerCellStyle = FindAppStyle("AppDataGridCenterCellStyle");
        var centerHeaderStyle = FindAppStyle("AppDataGridCenterColumnHeaderStyle");
        var readOnlyCheckBoxStyle = FindAppStyle("AppDataGridReadOnlyCheckBoxStyle");

        PreviewGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "是否有效",
            Binding = new System.Windows.Data.Binding(nameof(ImportPreviewRowGridItem.IsValid)),
            CellStyle = centerCellStyle,
            HeaderStyle = centerHeaderStyle,
            ElementStyle = readOnlyCheckBoxStyle,
            Width = 90
        });

        foreach (var field in fields.Where(x => !IsSystemField(x.FieldCode)))
        {
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = field.FieldName,
                Binding = new System.Windows.Data.Binding($"Data[{field.FieldCode}]"),
                Width = 150
            });
        }

        PreviewGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "错误信息",
            Binding = new System.Windows.Data.Binding(nameof(ImportPreviewRowGridItem.ErrorMessage)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 220,
            ElementStyle = BuildWrappingTextStyle()
        });
    }

    private static Style FindAppStyle(string resourceKey)
    {
        return (Style)System.Windows.Application.Current.FindResource(resourceKey);
    }

    private static Style BuildWrappingTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4)));
        return style;
    }

    private static bool IsSystemField(string fieldCode)
    {
        return string.Equals(fieldCode, "batch_no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldCode, "serial_no", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ApplyFilterAsync()
    {
        var token = ResetCancellation(ref _filterCts);
        var keyword = PreviewSearchBox?.Text.Trim() ?? string.Empty;
        var onlyErrors = OnlyErrorRowsBox.IsChecked == true;

        try
        {
            var rows = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                IEnumerable<ImportPreviewRowGridItem> query = onlyErrors
                    ? _rows.Where(x => !x.IsValid)
                    : _rows;

                if (!string.IsNullOrWhiteSpace(keyword))
                    query = query.Where(x => MatchesKeyword(x, keyword));

                var result = query.ToList();
                token.ThrowIfCancellationRequested();
                return result;
            }, token);

            if (token.IsCancellationRequested ||
                (PreviewSearchBox?.Text.Trim() ?? string.Empty) != keyword ||
                (OnlyErrorRowsBox.IsChecked == true) != onlyErrors)
            {
                return;
            }

            PreviewGrid.ItemsSource = rows;
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool MatchesKeyword(ImportPreviewRowGridItem row, string keyword)
    {
        if (row.RowIndex.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(row.ErrorMessage) &&
            row.ErrorMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return true;

        return row.Data.Values.Any(x =>
            !string.IsNullOrWhiteSpace(x) &&
            x.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelAndDispose(ref _filterCts);
        base.OnClosed(e);
    }

    private static CancellationToken ResetCancellation(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        return cts.Token;
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
