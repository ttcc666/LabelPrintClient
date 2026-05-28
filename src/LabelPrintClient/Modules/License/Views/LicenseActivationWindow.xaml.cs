using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;
using LabelPrintClient.Modules.License.Services;

namespace LabelPrintClient.Modules.License.Views;

public partial class LicenseActivationWindow : HandyControl.Controls.Window
{
    private readonly AppSettings _settings;

    public LicenseActivationWindow(AppSettings settings, LicenseResult? initialResult = null)
    {
        _settings = settings;
        InitializeComponent();
        MachineCodeBox.Text = LicenseManager.MachineCode;
        ProductCodeBox.Text = settings.ProductCode;
        LicenseFilePathBox.Text = settings.StandaloneLicenseFilePath;
        LicenseServerUrlBox.Text = settings.LicenseServerUrl;
        LicenseAccessKeyBox.Password = settings.LicenseAccessKey;
        SelectMode(settings.LicenseMode);
        StatusText.Text = initialResult?.Message ?? string.Empty;
    }

    private void BrowseLicenseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "License files|*.json;*.license|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
            LicenseFilePathBox.Text = dialog.FileName;
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out var errorMessage))
        {
            StatusText.Text = errorMessage;
            return;
        }

        AppConfigService.Save(_settings);
        LicenseManager.Initialize(_settings);
        var result = await LicenseManager.ValidateStartupAsync();
        if (result.IsValid)
        {
            DialogResult = true;
            return;
        }

        StatusText.Text = result.Message;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private bool TryBuildSettings(out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!TryGetSelectedMode(out var mode))
        {
            errorMessage = "请选择授权模式。";
            return false;
        }

        var productCode = ProductCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            errorMessage = "产品编码不能为空。";
            return false;
        }

        _settings.LicenseMode = mode;
        _settings.ProductCode = productCode;
        _settings.StandaloneLicenseFilePath = LicenseFilePathBox.Text.Trim();
        _settings.LicenseServerUrl = LicenseServerUrlBox.Text.Trim();
        _settings.LicenseAccessKey = LicenseAccessKeyBox.Password.Trim();
        return true;
    }

    private void SelectMode(LicenseMode mode)
    {
        foreach (var item in LicenseModeBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                LicenseModeBox.SelectedItem = item;
                return;
            }
        }

        LicenseModeBox.SelectedIndex = 0;
    }

    private bool TryGetSelectedMode(out LicenseMode mode)
    {
        mode = LicenseMode.Standalone;
        return LicenseModeBox.SelectedItem is ComboBoxItem item &&
               Enum.TryParse(item.Tag?.ToString(), out mode);
    }
}
