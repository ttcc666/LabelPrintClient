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

        if (preview.InvalidRows == 0)
        {
            SuccessAlertCard.Visibility = Visibility.Visible;
            ErrorAlertCard.Visibility = Visibility.Collapsed;
            SuccessFileNameText.Text = preview.ExcelFileName;
            SuccessTotalText.Text = AppLanguageService.Format("ImportPreview.TotalRows", preview.TotalRows);
            SuccessValidText.Text = AppLanguageService.Format("ImportPreview.ValidRows", preview.ValidRows);
        }
        else
        {
            SuccessAlertCard.Visibility = Visibility.Collapsed;
            ErrorAlertCard.Visibility = Visibility.Visible;
            ErrorFileNameText.Text = preview.ExcelFileName;
            ErrorTotalText.Text = AppLanguageService.Format("ImportPreview.TotalRows", preview.TotalRows);
            ErrorInvalidText.Text = AppLanguageService.Format("ImportPreview.InvalidRows", preview.InvalidRows);
        }

        ConfirmButton.IsEnabled = preview.InvalidRows == 0;
        ImportHintText.Text = preview.InvalidRows == 0
            ? AppLanguageService.GetString("ImportPreview.ConfirmHint")
            : AppLanguageService.GetString("ImportPreview.InvalidHint");
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
            AppMessageBox.Show(AppLanguageService.GetString("ImportPreview.CannotConfirmInvalidRows"), AppLanguageService.GetString("ImportPreview.ValidationTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Header = AppLanguageService.GetString("PrintCenter.IsValid"),
            Binding = new System.Windows.Data.Binding(nameof(ImportPreviewRowGridItem.IsValid)),
            CellStyle = centerCellStyle,
            HeaderStyle = centerHeaderStyle,
            ElementStyle = readOnlyCheckBoxStyle,
            Width = 90
        });

        foreach (var field in fields.Where(x => !TemplateSystemFields.IsSystemField(x.FieldCode)))
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
            Header = AppLanguageService.GetString("PrintHistory.ErrorMessage"),
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
