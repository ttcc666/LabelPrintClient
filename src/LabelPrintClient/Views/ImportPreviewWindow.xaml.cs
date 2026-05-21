using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Models;
using LabelPrintClient.Services.Import;
using LabelPrintClient.ViewModels;

namespace LabelPrintClient.Views;

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
                Data = x.Data
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
            Header = "Excel行",
            Binding = new System.Windows.Data.Binding(nameof(ImportPreviewRowGridItem.RowIndex)),
            Width = 80
        });
        PreviewGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "有效",
            Binding = new System.Windows.Data.Binding(nameof(ImportPreviewRowGridItem.IsValid)),
            Width = 70
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
        var rows = OnlyErrorRowsBox.IsChecked == true
            ? _rows.Where(x => !x.IsValid).ToList()
            : _rows;

        PreviewGrid.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
