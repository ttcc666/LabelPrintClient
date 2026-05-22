using System.IO;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.Settings.Services;
using LabelPrintClient.Services;
using WinForms = System.Windows.Forms;

namespace LabelPrintClient.Modules.Settings.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private const int MaxPrintCopies = 999;
    private const string SystemDefaultPrinterText = "使用系统默认打印机";

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadPrinters();
            LoadSettings();
        };
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
    }

    private void BrowseTemplateFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择本地模板保存目录",
            UseDescriptionForTitle = true,
            SelectedPath = ResolveInitialFolder(LocalTemplateFolderBox.Text)
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            LocalTemplateFolderBox.Text = dialog.SelectedPath;
        }
    }

    private async void BackupData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "备份文件|*.zip",
            FileName = SqliteBackupService.GetDefaultBackupFileName()
        };
        if (dialog.ShowDialog() != true)
            return;

        var success = await RunBackupOperationAsync(
            sender,
            "正在备份数据...",
            async token => await SqliteBackupService.CreateBackupAsync(dialog.FileName, App.Settings, token));
        if (!success)
            return;

        StatusText.Text = $"备份完成：{dialog.FileName}";
        AppMessageBox.Show($"备份完成：\n{dialog.FileName}", "数据备份", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RestoreData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "备份文件|*.zip|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;

        if (AppMessageBox.Show("恢复备份会覆盖当前 SQLite 数据库、配置文件和本地模板目录。恢复前会自动生成一份 pre-restore 备份。\n确定继续？",
                "确认恢复",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await RunBackupOperationAsync(
            sender,
            "正在恢复数据...",
            async token => await SqliteBackupService.RestoreAsync(dialog.FileName, App.Settings, token));
        if (result == null)
            return;

        AppMessageBox.Show($"恢复完成，应用将重启。\n恢复前备份：\n{result.PreRestoreBackupPath}", "数据恢复", MessageBoxButton.OK, MessageBoxImage.Information);
        RestartApplication();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out var settings, out var errorMessage))
        {
            AppMessageBox.Show(errorMessage, "配置校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            AppConfigService.Save(settings);

            if (!AppThemeService.TryApplyAndSetRuntime(settings.ThemeMode, out var themeErrorMessage))
            {
                StatusText.Text = $"配置已保存，但主题应用失败：{themeErrorMessage}";
                AppMessageBox.Show($"配置已保存，但主题应用失败：{themeErrorMessage}", "主题切换", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyToRuntimeSettings(settings);
            StatusText.Text = $"配置已保存：{DateTime.Now:HH:mm:ss}。主题已应用，其余运行配置重启后完全生效。";
            AppMessageBox.Show("配置已保存。运行模式、数据库连接和模板目录相关配置需要重启应用后完全生效。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"保存配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestSqliteConnection_Click(object sender, RoutedEventArgs e)
    {
        await TestConnectionAsync(sender, AppRunMode.LocalSqlite, SqliteConnectionBox.Text.Trim(), "SQLite");
    }

    private async void TestPostgreSqlConnection_Click(object sender, RoutedEventArgs e)
    {
        await TestConnectionAsync(sender, AppRunMode.LanPostgreSql, PostgreSqlConnectionBox.Text.Trim(), "PostgreSQL");
    }

    private void LoadSettings()
    {
        try
        {
            ConfigPathText.Text = AppConfigService.GetConfigPath();
            FillForm(AppConfigService.LoadOrCreateDefault());
            UpdateBackupState();
            StatusText.Text = $"配置已加载：{DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"加载配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FillForm(AppSettings settings)
    {
        SelectRunMode(settings.RunMode);
        SqliteConnectionBox.Text = settings.SqliteConnection;
        PostgreSqlConnectionBox.Text = settings.PostgreSqlConnection;
        LocalTemplateFolderBox.Text = settings.LocalTemplateFolder;
        OperatorNameBox.Text = settings.OperatorName;
        SelectDefaultPrinter(settings.DefaultPrinterName);
        DefaultPrintCopiesBox.Text = Math.Clamp(settings.DefaultPrintCopies, 1, MaxPrintCopies).ToString();
        ConfirmBeforePrintBox.IsChecked = settings.ConfirmBeforePrint;
        SelectThemeMode(settings.ThemeMode);
    }

    private bool TryBuildSettings(out AppSettings settings, out string errorMessage)
    {
        settings = new AppSettings();
        errorMessage = string.Empty;

        if (!TryGetSelectedRunMode(out var runMode))
        {
            errorMessage = "请选择运行模式。";
            return false;
        }

        var sqliteConnection = SqliteConnectionBox.Text.Trim();
        var postgreSqlConnection = PostgreSqlConnectionBox.Text.Trim();
        var localTemplateFolder = LocalTemplateFolderBox.Text.Trim();
        var operatorName = OperatorNameBox.Text.Trim();
        var defaultPrinterName = GetSelectedDefaultPrinterName();
        var themeMode = GetSelectedThemeMode();

        if (runMode == AppRunMode.LocalSqlite && string.IsNullOrWhiteSpace(sqliteConnection))
        {
            errorMessage = "本地 SQLite 模式下，SQLite 连接串不能为空。";
            return false;
        }

        if (runMode == AppRunMode.LanPostgreSql && string.IsNullOrWhiteSpace(postgreSqlConnection))
        {
            errorMessage = "局域网 PostgreSQL 模式下，PostgreSQL 连接串不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(localTemplateFolder))
        {
            errorMessage = "本地模板目录不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(operatorName))
        {
            errorMessage = "操作人不能为空。";
            return false;
        }

        if (!int.TryParse(DefaultPrintCopiesBox.Text.Trim(), out var defaultPrintCopies) ||
            defaultPrintCopies < 1 ||
            defaultPrintCopies > MaxPrintCopies)
        {
            errorMessage = $"默认打印份数必须是 1 到 {MaxPrintCopies} 之间的整数。";
            return false;
        }

        settings.RunMode = runMode;
        settings.SqliteConnection = sqliteConnection;
        settings.PostgreSqlConnection = postgreSqlConnection;
        settings.LocalTemplateFolder = localTemplateFolder;
        settings.OperatorName = operatorName;
        settings.DefaultPrinterName = defaultPrinterName;
        settings.DefaultPrintCopies = defaultPrintCopies;
        settings.ConfirmBeforePrint = ConfirmBeforePrintBox.IsChecked == true;
        settings.EnableSqlLogging = App.Settings.EnableSqlLogging;
        settings.ThemeMode = themeMode;
        return true;
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
        if (RunModeBox.SelectedItem is not ComboBoxItem item)
            return false;

        return Enum.TryParse(item.Tag?.ToString(), out runMode);
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
        if (ThemeModeBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AppThemeMode>(item.Tag?.ToString(), out var themeMode))
        {
            return themeMode;
        }

        return AppThemeMode.System;
    }

    private static void ApplyToRuntimeSettings(AppSettings settings)
    {
        App.Settings.RunMode = settings.RunMode;
        App.Settings.SqliteConnection = settings.SqliteConnection;
        App.Settings.PostgreSqlConnection = settings.PostgreSqlConnection;
        App.Settings.LocalTemplateFolder = settings.LocalTemplateFolder;
        App.Settings.OperatorName = settings.OperatorName;
        App.Settings.DefaultPrinterName = settings.DefaultPrinterName;
        App.Settings.DefaultPrintCopies = settings.DefaultPrintCopies;
        App.Settings.ConfirmBeforePrint = settings.ConfirmBeforePrint;
        App.Settings.EnableSqlLogging = settings.EnableSqlLogging;
        App.Settings.ThemeMode = settings.ThemeMode;
    }

    private void UpdateBackupState()
    {
        var isLocalSqlite = App.Settings.RunMode == AppRunMode.LocalSqlite;
        BackupDataButton.IsEnabled = isLocalSqlite;
        RestoreDataButton.IsEnabled = isLocalSqlite;
        BackupModeText.Text = isLocalSqlite
            ? "当前运行模式支持备份恢复。备份包包含 SQLite 数据库、appsettings.json 和本地模板目录。"
            : "一键备份恢复仅支持 LocalSqlite 模式。";
    }

    private async Task TestConnectionAsync(object sender, AppRunMode runMode, string connectionString, string displayName)
    {
        var testButton = sender as UIElement;
        if (testButton != null)
            testButton.IsEnabled = false;

        StatusText.Text = $"正在测试 {displayName} 连接...";

        try
        {
            await ConnectionTestService.TestAsync(runMode, connectionString);
            StatusText.Text = $"{displayName} 连接测试成功：{DateTime.Now:HH:mm:ss}";
            AppMessageBox.Show($"{displayName} 连接测试成功。", "连接测试", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{displayName} 连接测试失败：{ex.Message}";
            AppMessageBox.Show($"{displayName} 连接测试失败：{ex.Message}", "连接测试", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (testButton != null)
                testButton.IsEnabled = true;
        }
    }

    private async Task<bool> RunBackupOperationAsync(object sender, string runningText, Func<CancellationToken, Task> operation)
    {
        var result = await RunBackupOperationAsync<object>(sender, runningText, async token =>
        {
            await operation(token);
            return new object();
        });
        return result != null;
    }

    private async Task<T?> RunBackupOperationAsync<T>(object sender, string runningText, Func<CancellationToken, Task<T>> operation)
        where T : class
    {
        var element = sender as UIElement;
        if (element != null)
            element.IsEnabled = false;

        StatusText.Text = runningText;

        try
        {
            return await operation(CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"操作失败：{ex.Message}";
            AppMessageBox.Show(ex.Message, "数据备份 / 恢复", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
            UpdateBackupState();
        }
    }

    private static void RestartApplication()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath))
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

        System.Windows.Application.Current.Shutdown();
    }

    private static string ResolveInitialFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return AppContext.BaseDirectory;

        var path = Path.IsPathRooted(folder)
            ? folder
            : Path.Combine(AppContext.BaseDirectory, folder);

        return Directory.Exists(path) ? path : AppContext.BaseDirectory;
    }
}
