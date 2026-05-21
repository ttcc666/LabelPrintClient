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
        ApplyFilter();
    }

    private void OnlyErrorRowsBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        PreviewSearchBox.Text = string.Empty;
        OnlyErrorRowsBox.IsChecked = false;
        ApplyFilter();
    }

    private void PreviewSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        ApplyFilter();
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

    private void ApplyFilter()
    {
        var keyword = PreviewSearchBox?.Text.Trim() ?? string.Empty;
        var rows = OnlyErrorRowsBox.IsChecked == true
            ? _rows.Where(x => !x.IsValid).ToList()
            : _rows;

        if (!string.IsNullOrWhiteSpace(keyword))
            rows = rows.Where(x => MatchesKeyword(x, keyword)).ToList();

        PreviewGrid.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
}


