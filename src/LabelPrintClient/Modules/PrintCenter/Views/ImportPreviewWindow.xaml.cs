using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.PrintCenter.ViewModels;

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
        SummaryText.Text = $"{preview.ExcelFileName} · 共 {preview.TotalRows} 行，有效 {preview.ValidRows} 行，错误 {preview.InvalidRows} 行";
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

        PreviewGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Excel行号",
            Binding = new System.Windows.Data.Binding(nameof(ImportPreviewRowGridItem.RowIndex)),
            Width = 100
        });
        PreviewGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "是否有效",
            Binding = new System.Windows.Data.Binding(nameof(ImportPreviewRowGridItem.IsValid)),
            CellStyle = centerCellStyle,
            HeaderStyle = centerHeaderStyle,
            ElementStyle = readOnlyCheckBoxStyle,
            Width = 90
        });

        foreach (var field in fields)
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
            MinWidth = 180
        });
    }

    private static Style FindAppStyle(string resourceKey)
    {
        return (Style)System.Windows.Application.Current.FindResource(resourceKey);
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


