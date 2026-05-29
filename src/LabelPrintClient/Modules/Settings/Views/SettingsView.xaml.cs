using System.IO;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Modules.Auth.Services;
using LabelPrintClient.Modules.License.Models;
using LabelPrintClient.Modules.License.Services;
using LabelPrintClient.Modules.Settings.Services;
using LabelPrintClient.Modules.Update.Services;
using LabelPrintClient.Services;
using WinForms = System.Windows.Forms;

namespace LabelPrintClient.Modules.Settings.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private const int MaxPrintCopies = 999;
    private static string SystemDefaultPrinterText => AppLanguageService.GetString("Settings.SystemDefaultPrinter");

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
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsReload, "重新加载配置")) return;
        LoadSettings();
    }

    private void BrowseTemplateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsBrowseTemplateFolder, "浏览模板目录")) return;

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = AppLanguageService.GetString("Settings.SelectTemplateFolder"),
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
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsBackup, "一键备份")) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = AppLanguageService.GetString("Settings.BackupFileFilter"),
            FileName = SqliteBackupService.GetDefaultBackupFileName()
        };
        if (dialog.ShowDialog() != true)
            return;

        var success = await RunBackupOperationAsync(
            sender,
            AppLanguageService.GetString("Settings.BackupRunning"),
            async token => await SqliteBackupService.CreateBackupAsync(dialog.FileName, App.Settings, token));
        if (!success)
            return;

        StatusText.Text = AppLanguageService.Format("Settings.BackupCompletedStatus", dialog.FileName);
        AppMessageBox.Show(AppLanguageService.Format("Settings.BackupCompletedMessage", dialog.FileName), AppLanguageService.GetString("Settings.BackupTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RestoreData_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsRestore, "恢复备份")) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = AppLanguageService.GetString("Settings.BackupOpenFileFilter")
        };
        if (dialog.ShowDialog() != true)
            return;

        if (AppMessageBox.Show(AppLanguageService.GetString("Settings.RestoreConfirmMessage"),
                AppLanguageService.GetString("Settings.RestoreConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await RunBackupOperationAsync(
            sender,
            AppLanguageService.GetString("Settings.RestoreRunning"),
            async token => await SqliteBackupService.RestoreAsync(dialog.FileName, App.Settings, token));
        if (result == null)
            return;

        AppMessageBox.Show(AppLanguageService.Format("Settings.RestoreCompletedMessage", result.PreRestoreBackupPath), AppLanguageService.GetString("Settings.RestoreTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        RestartApplication();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsSave, "保存配置")) return;

        if (!TryBuildSettings(out var settings, out var errorMessage))
        {
            AppMessageBox.Show(errorMessage, AppLanguageService.GetString("Settings.ValidationFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            bool dbChanged = settings.RunMode != App.Settings.RunMode ||
                             settings.SqliteConnection != App.Settings.SqliteConnection ||
                             settings.PostgreSqlConnection != App.Settings.PostgreSqlConnection;

            if (dbChanged)
            {
                // 1. 尝试使用新连接进行可用性预测试，若连通失败则会抛异常被 catch，不予写入配置
                StatusText.Text = AppLanguageService.GetString("Settings.DatabaseSwitchingPending");
                var testConnectionString = settings.RunMode == AppRunMode.LocalSqlite
                    ? settings.SqliteConnection
                    : settings.PostgreSqlConnection;

                await ConnectionTestService.TestAsync(settings.RunMode, testConnectionString);
            }

            // 2. 预校验连通成功，写入物理 appsettings.json 配置文件
            AppConfigService.Save(settings);

            if (!AppThemeService.TryApplyAndSetRuntime(settings.ThemeMode, out var themeErrorMessage))
            {
                StatusText.Text = AppLanguageService.Format("Settings.ThemeSwitchFailed", themeErrorMessage);
                AppMessageBox.Show(AppLanguageService.Format("Settings.ThemeSwitchFailed", themeErrorMessage), AppLanguageService.GetString("Settings.ThemeSwitch"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!AppLanguageService.TryApplyAndSetRuntime(settings.Language, out var languageErrorMessage))
            {
                StatusText.Text = AppLanguageService.Format("Settings.LanguageSwitchFailed", languageErrorMessage);
                AppMessageBox.Show(AppLanguageService.Format("Settings.LanguageSwitchFailed", languageErrorMessage), AppLanguageService.GetString("Settings.LanguageSwitch"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (dbChanged)
            {
                // 3. 激活新数据库连接并应用架构建表、资源同步
                AppDb.Init(settings);
                DbInitializer.InitTables();
                await PermissionBootstrapper.SyncAsync().ConfigureAwait(true);

                // 4. 检索目标数据库是否已经拥有系统管理员用户
                var hasAdmin = await PermissionBootstrapper.HasAdministratorUserAsync().ConfigureAwait(true);

                ApplyToRuntimeSettings(settings);
                StatusText.Text = AppLanguageService.Format("Settings.Saved", DateTime.Now);

                if (!hasAdmin)
                {
                    // 检测到是空库，引导并强行重启，以便在下一次 OnStartup 时无缝展示 InitialAdminWindow
                    AppMessageBox.Show(
                        AppLanguageService.GetString("Settings.DbSwitchEmptyRestart"),
                        AppLanguageService.GetString("Settings.SaveSuccess"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    RestartApplication();
                    return;
                }
                else
                {
                    // 检测到已有管理员账号，为保证内存会话等上下文百分之百干净同步，优雅引导重启
                    AppMessageBox.Show(
                        AppLanguageService.GetString("Settings.DbSwitchSuccessRestart"),
                        AppLanguageService.GetString("Settings.SaveSuccess"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    RestartApplication();
                    return;
                }
            }

            ApplyToRuntimeSettings(settings);
            StatusText.Text = AppLanguageService.Format("Settings.Saved", DateTime.Now);
            AppMessageBox.Show(AppLanguageService.GetString("Settings.SavedMessage"), AppLanguageService.GetString("Settings.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = AppLanguageService.Format("Settings.SaveFailed", ex.Message);
            AppMessageBox.Show(AppLanguageService.Format("Settings.SaveFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestSqliteConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsTestConnection, "测试 SQLite 连接")) return;
        await TestConnectionAsync(sender, AppRunMode.LocalSqlite, SqliteConnectionBox.Text.Trim(), "SQLite");
    }

    private async void TestPostgreSqlConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsTestConnection, "测试 PostgreSQL 连接")) return;
        await TestConnectionAsync(sender, AppRunMode.LanPostgreSql, PostgreSqlConnectionBox.Text.Trim(), "PostgreSQL");
    }

    private void LoadSettings()
    {
        try
        {
            ConfigPathText.Text = AppConfigService.GetConfigPath();
            FillForm(AppConfigService.LoadOrCreateDefault());
            UpdateBackupState();
            UpdateLicenseStatusText();
            StatusText.Text = AppLanguageService.Format("Settings.Loaded", DateTime.Now);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(AppLanguageService.Format("Settings.LoadFailed", ex.Message), AppLanguageService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FillForm(AppSettings settings)
    {
        SelectRunMode(settings.RunMode);
        SqliteConnectionBox.Text = settings.SqliteConnection;
        PostgreSqlConnectionBox.Text = settings.PostgreSqlConnection;
        LocalTemplateFolderBox.Text = settings.LocalTemplateFolder;
        OperatorNameBox.Text = CurrentUserService.OperatorName;
        SelectDefaultPrinter(settings.DefaultPrinterName);
        DefaultPrintCopiesBox.Text = Math.Clamp(settings.DefaultPrintCopies, 1, MaxPrintCopies).ToString();
        ConfirmBeforePrintBox.IsChecked = settings.ConfirmBeforePrint;
        SelectThemeMode(settings.ThemeMode);
        SelectLanguage(settings.Language);
        SelectLicenseMode(settings.LicenseMode);
        ProductCodeBox.Text = settings.ProductCode;
        LicenseFilePathBox.Text = settings.StandaloneLicenseFilePath;
        LicenseServerUrlBox.Text = settings.LicenseServerUrl;
        LicenseAccessKeyBox.Password = settings.LicenseAccessKey;
        MachineCodeBox.Text = LicenseManager.MachineCode;
        CurrentVersionText.Text = ClientUpdateService.CurrentVersion;
        UpdateServerUrlBox.Text = settings.UpdateServerUrl;
        UpdateChannelBox.Text = settings.UpdateChannel;
        AutoCheckUpdatesBox.IsChecked = settings.AutoCheckUpdates;
    }

    private bool TryBuildSettings(out AppSettings settings, out string errorMessage)
    {
        settings = new AppSettings();
        errorMessage = string.Empty;

        if (!TryGetSelectedRunMode(out var runMode))
        {
            errorMessage = AppLanguageService.GetString("Settings.RunModeRequired");
            return false;
        }

        var sqliteConnection = SqliteConnectionBox.Text.Trim();
        var postgreSqlConnection = PostgreSqlConnectionBox.Text.Trim();
        var localTemplateFolder = LocalTemplateFolderBox.Text.Trim();
        var defaultPrinterName = GetSelectedDefaultPrinterName();
        var themeMode = GetSelectedThemeMode();
        var language = GetSelectedLanguage();
        var licenseMode = GetSelectedLicenseMode();
        var productCode = ProductCodeBox.Text.Trim();
        var licenseFilePath = LicenseFilePathBox.Text.Trim();
        var licenseServerUrl = LicenseServerUrlBox.Text.Trim();
        var licenseAccessKey = LicenseAccessKeyBox.Password.Trim();
        var updateServerUrl = UpdateServerUrlBox.Text.Trim();
        var updateChannel = UpdateChannelBox.Text.Trim();
        var autoCheckUpdates = AutoCheckUpdatesBox.IsChecked == true;

        if (runMode == AppRunMode.LocalSqlite && string.IsNullOrWhiteSpace(sqliteConnection))
        {
            errorMessage = AppLanguageService.GetString("Settings.SqliteConnectionRequired");
            return false;
        }

        if (runMode == AppRunMode.LanPostgreSql && string.IsNullOrWhiteSpace(postgreSqlConnection))
        {
            errorMessage = AppLanguageService.GetString("Settings.PostgreSqlConnectionRequired");
            return false;
        }

        if (string.IsNullOrWhiteSpace(localTemplateFolder))
        {
            errorMessage = AppLanguageService.GetString("Settings.TemplateFolderRequired");
            return false;
        }

        if (string.IsNullOrWhiteSpace(productCode))
        {
            errorMessage = AppLanguageService.GetString("License.ProductCodeRequired");
            return false;
        }

        if (!int.TryParse(DefaultPrintCopiesBox.Text.Trim(), out var defaultPrintCopies) ||
            defaultPrintCopies < 1 ||
            defaultPrintCopies > MaxPrintCopies)
        {
            errorMessage = AppLanguageService.Format("Settings.PrintCopiesRange", MaxPrintCopies);
            return false;
        }

        if (string.IsNullOrWhiteSpace(updateServerUrl) ||
            !Uri.TryCreate(updateServerUrl, UriKind.Absolute, out var parsedUpdateServerUrl) ||
            (parsedUpdateServerUrl.Scheme != Uri.UriSchemeHttp && parsedUpdateServerUrl.Scheme != Uri.UriSchemeHttps))
        {
            errorMessage = AppLanguageService.GetString("Update.ServerUrlInvalid");
            return false;
        }

        if (string.IsNullOrWhiteSpace(updateChannel))
        {
            errorMessage = AppLanguageService.GetString("Update.ChannelRequired");
            return false;
        }

        settings.RunMode = runMode;
        settings.SqliteConnection = sqliteConnection;
        settings.PostgreSqlConnection = postgreSqlConnection;
        settings.LocalTemplateFolder = localTemplateFolder;
        settings.OperatorName = App.Settings.OperatorName;
        settings.DefaultPrinterName = defaultPrinterName;
        settings.DefaultPrintCopies = defaultPrintCopies;
        settings.ConfirmBeforePrint = ConfirmBeforePrintBox.IsChecked == true;
        settings.EnableSqlLogging = App.Settings.EnableSqlLogging;
        settings.ThemeMode = themeMode;
        settings.Language = language;
        settings.LicenseMode = licenseMode;
        settings.ProductCode = productCode;
        settings.StandaloneLicenseFilePath = licenseFilePath;
        settings.LicenseServerUrl = licenseServerUrl;
        settings.LicenseAccessKey = licenseAccessKey;
        settings.LicenseHeartbeatIntervalSeconds = App.Settings.LicenseHeartbeatIntervalSeconds;
        settings.LicenseHeartbeatTimeoutSeconds = App.Settings.LicenseHeartbeatTimeoutSeconds;
        settings.UpdateServerUrl = updateServerUrl;
        settings.UpdateChannel = updateChannel;
        settings.AutoCheckUpdates = autoCheckUpdates;
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

    private void SelectLanguage(AppLanguage language)
    {
        foreach (var item in LanguageBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), language.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                LanguageBox.SelectedItem = item;
                return;
            }
        }

        LanguageBox.SelectedIndex = 0;
    }

    private AppLanguage GetSelectedLanguage()
    {
        if (LanguageBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AppLanguage>(item.Tag?.ToString(), out var language))
        {
            return language;
        }

        return AppLanguage.ZhCn;
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

    private LicenseMode GetSelectedLicenseMode()
    {
        if (LicenseModeBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<LicenseMode>(item.Tag?.ToString(), out var mode))
        {
            return mode;
        }

        return LicenseMode.Standalone;
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
        App.Settings.Language = settings.Language;
        App.Settings.LicenseMode = settings.LicenseMode;
        App.Settings.ProductCode = settings.ProductCode;
        App.Settings.LicenseServerUrl = settings.LicenseServerUrl;
        App.Settings.LicenseAccessKey = settings.LicenseAccessKey;
        App.Settings.StandaloneLicenseFilePath = settings.StandaloneLicenseFilePath;
        App.Settings.LicenseHeartbeatIntervalSeconds = settings.LicenseHeartbeatIntervalSeconds;
        App.Settings.LicenseHeartbeatTimeoutSeconds = settings.LicenseHeartbeatTimeoutSeconds;
        App.Settings.UpdateServerUrl = settings.UpdateServerUrl;
        App.Settings.UpdateChannel = settings.UpdateChannel;
        App.Settings.AutoCheckUpdates = settings.AutoCheckUpdates;
        LicenseManager.Initialize(App.Settings);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsSave, "检查客户端更新")) return;

        if (!TryBuildSettings(out var settings, out var errorMessage))
        {
            AppMessageBox.Show(errorMessage, AppLanguageService.GetString("Settings.ValidationFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var element = sender as UIElement;
        if (element != null)
            element.IsEnabled = false;

        StatusText.Text = AppLanguageService.GetString("Update.Checking");
        try
        {
            await ClientUpdateService.CheckAndPromptAsync(settings, manual: true);
            StatusText.Text = AppLanguageService.Format("Update.Checked", DateTime.Now);
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
        }
    }

    private void BrowseLicenseFile_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsSave, "导入授权文件")) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "License files|*.json;*.license|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
            LicenseFilePathBox.Text = dialog.FileName;
    }

    private async void ValidateLicense_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.EnsurePermission(Permissions.SettingsSave, "重新校验授权")) return;

        if (!TryBuildSettings(out var settings, out var errorMessage))
        {
            AppMessageBox.Show(errorMessage, AppLanguageService.GetString("Settings.ValidationFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var element = sender as UIElement;
        if (element != null)
            element.IsEnabled = false;

        try
        {
            LicenseManager.Initialize(settings);
            var result = await LicenseManager.ValidateStartupAsync();
            LicenseStatusText.Text = result.Message;
            LicenseStatusText.Foreground = result.IsValid
                ? System.Windows.Media.Brushes.ForestGreen
                : System.Windows.Media.Brushes.IndianRed;
        }
        finally
        {
            if (element != null)
                element.IsEnabled = true;
        }
    }

    private void UpdateLicenseStatusText()
    {
        var result = LicenseManager.Current;
        LicenseStatusText.Text = result == null
            ? AppLanguageService.GetString("License.StatusUnknown")
            : result.Message;
        LicenseStatusText.Foreground = result?.IsValid == true
            ? System.Windows.Media.Brushes.ForestGreen
            : System.Windows.Media.Brushes.IndianRed;
    }

    private void UpdateBackupState()
    {
        var isLocalSqlite = App.Settings.RunMode == AppRunMode.LocalSqlite;
        BackupDataButton.IsEnabled = isLocalSqlite;
        RestoreDataButton.IsEnabled = isLocalSqlite;
        BackupModeText.Text = isLocalSqlite
            ? AppLanguageService.GetString("Settings.BackupSupported")
            : AppLanguageService.GetString("Settings.BackupUnsupported");
    }

    private async Task TestConnectionAsync(object sender, AppRunMode runMode, string connectionString, string displayName)
    {
        var testButton = sender as UIElement;
        if (testButton != null)
            testButton.IsEnabled = false;

        StatusText.Text = AppLanguageService.Format("Settings.TestingConnection", displayName);

        try
        {
            await ConnectionTestService.TestAsync(runMode, connectionString);
            StatusText.Text = AppLanguageService.Format("Settings.ConnectionTestSuccessStatus", displayName, DateTime.Now);
            AppMessageBox.Show(AppLanguageService.Format("Settings.ConnectionTestSuccessMessage", displayName), AppLanguageService.GetString("Settings.ConnectionTestTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = AppLanguageService.Format("Settings.ConnectionTestFailedStatus", displayName, ex.Message);
            AppMessageBox.Show(AppLanguageService.Format("Settings.ConnectionTestFailedMessage", displayName, ex.Message), AppLanguageService.GetString("Settings.ConnectionTestTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            StatusText.Text = AppLanguageService.Format("Common.OperationFailed", ex.Message);
            AppMessageBox.Show(ex.Message, AppLanguageService.GetString("Settings.BackupRestoreTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
