using System.Windows;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.PrintCenter.Views;

public partial class BatchNoInputWindow : Window
{
    public string BatchNo { get; private set; } = string.Empty;

    public BatchNoInputWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => BatchNoBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var batchNo = BatchNoBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(batchNo))
        {
            AppMessageBox.Show(AppLanguageService.GetString("BatchNo.Required"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BatchNo = batchNo;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
