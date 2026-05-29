using System.IO;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;
using LabelPrintClient.Modules.License.Services;
using LabelPrintClient.Modules.Settings.Services;
using LabelPrintClient.Services;
using WinForms = System.Windows.Forms;

namespace LabelPrintClient.Modules.Settings.Views;

public partial class FirstRunConfigWindow : HandyControl.Controls.Window
{
    private const int MaxPrintCopies = 999;

    private static string SystemDefaultPrinterText => AppLanguageService.GetString("Settings.SystemDefaultPrinter");

    public FirstRunConfigWindow(AppSettings settings)
    {
        InitializeComponent();
        ConfigPathText.Text = $"{AppLanguageService.GetString("Setup.ConfigPath")}: {AppConfigService.GetConfigPath()}";
        MachineCodeBox.Text = LicenseManager.MachineCode;
        LoadPrinters();
        FillForm(settings);
        UpdateRunModeVisibility();
        UpdateLicenseModeVisibility();
        StatusText.Text = AppLanguageService.GetString("Setup.StatusReady");
    }

    private void FillForm(AppSettings settings)
    {
        SelectRunMode(settings.RunMode);
        SqliteDatabasePathBox.Text = AppConfigService.GetSqliteDatabasePath(settings.SqliteConnection);
        PostgreSqlConnectionBox.Text = settings.PostgreSqlConnection;
        TemplateFolderBox.Text = AppConfigService.ResolveTemplateFolder(settings.LocalTemplateFolder);
        SelectDefaultPrinter(settings.DefaultPrinterName);
        DefaultPrintCopiesBox.Text = Math.Clamp(settings.DefaultPrintCopies, 1, MaxPrintCopies).ToString();
        ConfirmBeforePrintBox.IsChecked = settings.ConfirmBeforePrint;
        SelectLicenseMode(settings.LicenseMode);
        ProductCodeBox.Text = settings.ProductCode;
        LicenseFilePathBox.Text = string.IsNullOrWhiteSpace(settings.StandaloneLicenseFilePath)
            ? AppConfigService.GetDefaultStandaloneLicenseFilePath()
            : AppConfigService.ResolveStandaloneLicenseFilePath(settings.StandaloneLicenseFilePath);
        LicenseServerUrlBox.Text = settings.LicenseServerUrl;
        LicenseAccessKeyBox.Password = settings.LicenseAccessKey;
        UpdateServerUrlBox.Text = settings.UpdateServerUrl;
        UpdateChannelBox.Text = settings.UpdateChannel;
        AutoCheckUpdatesBox.IsChecked = settings.AutoCheckUpdates;
        SelectThemeMode(settings.ThemeMode);
        SelectLanguage(settings.Language);
    }

    private void LoadPrinters()
    {
        var printers = PrinterSettings.InstalledPrinters
            .Cast<string>()
            .OrderBy(x => x)
            .ToList();

        printers.Insert(0, SystemDefaultPrinterText);
        DefaultPrinterBox.ItemsSource = printers;
    }

    private void BrowseSqliteDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "SQLite database|*.db|All files|*.*",
            FileName = "label_print.db",
            InitialDirectory = GetExistingDirectoryOrDefault(SqliteDatabasePathBox.Text, AppConfigService.GetDefaultDataDirectory())
        };

        if (dialog.ShowDialog(this) == true)
            SqliteDatabasePathBox.Text = dialog.FileName;
    }

    private void BrowseTemplateFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = AppLanguageService.GetString("Settings.SelectTemplateFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = GetExistingDirectoryOrDefault(TemplateFolderBox.Text, AppConfigService.GetDefaultTemplateFolder())
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            TemplateFolderBox.Text = dialog.SelectedPath;
    }

    private void BrowseLicenseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "License files|*.json;*.license|All files|*.*",
            InitialDirectory = GetExistingDirectoryOrDefault(LicenseFilePathBox.Text, AppConfigService.GetMachineDataDirectory())
        };

        if (dialog.ShowDialog(this) == true)
            LicenseFilePathBox.Text = dialog.FileName;
    }

    private async void TestDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildDatabaseInput(out var runMode, out var connectionString, out var displayName, out var errorMessage))
        {
            ShowValidationError(errorMessage);
            return;
        }

        await TestDatabaseAsync(runMode, connectionString, displayName, sender as UIElement);
    }

    private async void SaveAndContinue_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out var settings, out var errorMessage))
        {
            ShowValidationError(errorMessage);
            return;
        }

        var element = sender as UIElement;
        if (element != null)
            element.IsEnabled = false;

        try
        {
            StatusText.Text = AppLanguageService.GetString("Setup.DatabaseTesting");
            var connectionString = settings.RunMode == AppRunMode.LocalSqlite
                ? settings.SqliteConnection
                : settings.PostgreSqlConnection;
            await ConnectionTestService.TestAsync(settings.RunMode, connectionString);

            AppConfigService.Save(settings);
            StatusText.Text = AppLanguageService.GetString("Setup.Saved");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowValidationError(AppLanguageService.Format("Settings.SaveFailed", ex.Message));
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
        }
    }

    private async Task TestDatabaseAsync(AppRunMode runMode, string connectionString, string displayName, UIElement? element)
    {
        if (element != null)
            element.IsEnabled = false;

        try
        {
            StatusText.Text = AppLanguageService.Format("Settings.TestingConnection", displayName);
            await ConnectionTestService.TestAsync(runMode, connectionString);
            StatusText.Text = AppLanguageService.Format("Settings.ConnectionTestSuccessStatus", displayName, DateTime.Now);
            AppMessageBox.Show(
                AppLanguageService.Format("Settings.ConnectionTestSuccessMessage", displayName),
                AppLanguageService.GetString("Settings.ConnectionTestTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowValidationError(AppLanguageService.Format("Settings.ConnectionTestFailedMessage", displayName, ex.Message));
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
        }
    }

    private bool TryBuildSettings(out AppSettings settings, out string errorMessage)
    {
        settings = AppConfigService.CreateDefault();
        errorMessage = string.Empty;

        if (!TryBuildDatabaseInput(out var runMode, out var databaseConnectionString, out _, out errorMessage))
            return false;

        var templateFolder = TemplateFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(templateFolder))
        {
            errorMessage = AppLanguageService.GetString("Settings.TemplateFolderRequired");
            return false;
        }

        if (!TryGetSelectedLicenseMode(out var licenseMode))
        {
            errorMessage = AppLanguageService.GetString("License.ProductCodeRequired");
            return false;
        }

        var productCode = ProductCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            errorMessage = AppLanguageService.GetString("License.ProductCodeRequired");
            return false;
        }

        var licenseServerUrl = LicenseServerUrlBox.Text.Trim();
        if (licenseMode == LicenseMode.Floating && !IsValidHttpUrl(licenseServerUrl))
        {
            errorMessage = AppLanguageService.GetString("Update.ServerUrlInvalid");
            return false;
        }

        var updateServerUrl = UpdateServerUrlBox.Text.Trim();
        if (!IsValidHttpUrl(updateServerUrl))
        {
            errorMessage = AppLanguageService.GetString("Update.ServerUrlInvalid");
            return false;
        }

        var updateChannel = UpdateChannelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(updateChannel))
        {
            errorMessage = AppLanguageService.GetString("Update.ChannelRequired");
            return false;
        }

        if (!int.TryParse(DefaultPrintCopiesBox.Text.Trim(), out var defaultPrintCopies) ||
            defaultPrintCopies < 1 ||
            defaultPrintCopies > MaxPrintCopies)
        {
            errorMessage = AppLanguageService.Format("Settings.PrintCopiesRange", MaxPrintCopies);
            return false;
        }

        settings.RunMode = runMode;
        if (runMode == AppRunMode.LocalSqlite)
            settings.SqliteConnection = databaseConnectionString;
        else
            settings.PostgreSqlConnection = databaseConnectionString;

        settings.LocalTemplateFolder = AppConfigService.ResolveTemplateFolder(templateFolder);
        settings.DefaultPrinterName = GetSelectedDefaultPrinterName();
        settings.DefaultPrintCopies = defaultPrintCopies;
        settings.ConfirmBeforePrint = ConfirmBeforePrintBox.IsChecked == true;
        settings.ThemeMode = GetSelectedThemeMode();
        settings.Language = GetSelectedLanguage();
        settings.LicenseMode = licenseMode;
        settings.ProductCode = productCode;
        settings.StandaloneLicenseFilePath = BuildLicenseFilePath();
        settings.LicenseServerUrl = licenseServerUrl;
        settings.LicenseAccessKey = LicenseAccessKeyBox.Password.Trim();
        settings.UpdateServerUrl = updateServerUrl;
        settings.UpdateChannel = updateChannel;
        settings.AutoCheckUpdates = AutoCheckUpdatesBox.IsChecked == true;
        return true;
    }

    private bool TryBuildDatabaseInput(
        out AppRunMode runMode,
        out string connectionString,
        out string displayName,
        out string errorMessage)
    {
        runMode = AppRunMode.LocalSqlite;
        connectionString = string.Empty;
        displayName = "SQLite";
        errorMessage = string.Empty;

        if (!TryGetSelectedRunMode(out runMode))
        {
            errorMessage = AppLanguageService.GetString("Settings.RunModeRequired");
            return false;
        }

        if (runMode == AppRunMode.LocalSqlite)
        {
            var sqlitePath = SqliteDatabasePathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sqlitePath))
            {
                errorMessage = AppLanguageService.GetString("Settings.SqliteConnectionRequired");
                return false;
            }

            connectionString = AppConfigService.CreateSqliteConnection(AppConfigService.ResolveMachinePath(sqlitePath));
            displayName = "SQLite";
            return true;
        }

        connectionString = PostgreSqlConnectionBox.Text.Trim();
        displayName = "PostgreSQL";
        if (!string.IsNullOrWhiteSpace(connectionString))
            return true;

        errorMessage = AppLanguageService.GetString("Settings.PostgreSqlConnectionRequired");
        return false;
    }

    private string BuildLicenseFilePath()
    {
        var licenseFilePath = LicenseFilePathBox.Text.Trim();
        return string.IsNullOrWhiteSpace(licenseFilePath)
            ? string.Empty
            : AppConfigService.ResolveStandaloneLicenseFilePath(licenseFilePath);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void RunModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateRunModeVisibility();
    }

    private void LicenseModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLicenseModeVisibility();
    }

    private void UpdateRunModeVisibility()
    {
        if (!TryGetSelectedRunMode(out var runMode))
            return;

        var isSqlite = runMode == AppRunMode.LocalSqlite;
        SqlitePanel.IsEnabled = isSqlite;
        SqlitePanel.Opacity = isSqlite ? 1.0 : 0.45;
        PostgreSqlPanel.IsEnabled = !isSqlite;
        PostgreSqlPanel.Opacity = isSqlite ? 0.45 : 1.0;
    }

    private void UpdateLicenseModeVisibility()
    {
        if (!TryGetSelectedLicenseMode(out var mode))
            return;

        StandalonePanel.Visibility = mode == LicenseMode.Standalone ? Visibility.Visible : Visibility.Collapsed;
        FloatingPanel.Visibility = mode == LicenseMode.Floating ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectRunMode(AppRunMode runMode)
    {
        foreach (var item in RunModeBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), runMode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                RunModeBox.SelectedItem = item;
                return;
            }
        }

        RunModeBox.SelectedIndex = 0;
    }

    private bool TryGetSelectedRunMode(out AppRunMode runMode)
    {
        runMode = AppRunMode.LocalSqlite;
        return RunModeBox.SelectedItem is ComboBoxItem item &&
               Enum.TryParse(item.Tag?.ToString(), out runMode);
    }

    private void SelectLicenseMode(LicenseMode mode)
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

    private bool TryGetSelectedLicenseMode(out LicenseMode mode)
    {
        mode = LicenseMode.Standalone;
        return LicenseModeBox.SelectedItem is ComboBoxItem item &&
               Enum.TryParse(item.Tag?.ToString(), out mode);
    }

    private void SelectDefaultPrinter(string? printerName)
    {
        var printers = DefaultPrinterBox.Items.OfType<string>().ToList();
        if (printers.Count == 0)
            return;

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            var matched = printers.FirstOrDefault(x => string.Equals(x, printerName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matched))
            {
                DefaultPrinterBox.SelectedItem = matched;
                return;
            }
        }

        DefaultPrinterBox.SelectedIndex = 0;
    }

    private string? GetSelectedDefaultPrinterName()
    {
        var printerName = DefaultPrinterBox.SelectedItem as string;
        return string.IsNullOrWhiteSpace(printerName) ||
               string.Equals(printerName, SystemDefaultPrinterText, StringComparison.Ordinal)
            ? null
            : printerName.Trim();
    }

    private void SelectThemeMode(AppThemeMode themeMode)
    {
        foreach (var item in ThemeModeBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), themeMode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                ThemeModeBox.SelectedItem = item;
                return;
            }
        }

        ThemeModeBox.SelectedIndex = 0;
    }

    private AppThemeMode GetSelectedThemeMode()
    {
        return ThemeModeBox.SelectedItem is ComboBoxItem item &&
               Enum.TryParse<AppThemeMode>(item.Tag?.ToString(), out var themeMode)
            ? themeMode
            : AppThemeMode.System;
    }

    private void SelectLanguage(AppLanguage language)
    {
        var tag = language == AppLanguage.ZhCn ? "ZhCn" : "EnUs";
        foreach (var item in LanguageBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                LanguageBox.SelectedItem = item;
                return;
            }
        }

        LanguageBox.SelectedIndex = 0;
    }

    private AppLanguage GetSelectedLanguage()
    {
        if (LanguageBox.SelectedItem is not ComboBoxItem item ||
            item.Tag?.ToString() is not string tag)
        {
            return AppLanguage.ZhCn;
        }

        return tag.Equals("EnUs", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.EnUs
            : AppLanguage.ZhCn;
    }

    private void ShowValidationError(string message)
    {
        StatusText.Text = message;
        AppMessageBox.Show(
            message,
            AppLanguageService.GetString("Settings.ValidationFailedTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool IsValidHttpUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
               (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetExistingDirectoryOrDefault(string path, string defaultDirectory)
    {
        var resolvedPath = string.IsNullOrWhiteSpace(path)
            ? defaultDirectory
            : AppConfigService.ResolveMachinePath(path);
        var directory = Directory.Exists(resolvedPath)
            ? resolvedPath
            : Path.GetDirectoryName(resolvedPath);

        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : defaultDirectory;
    }
}
