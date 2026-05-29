using LabelPrintClient.Modules.License.Models;

namespace LabelPrintClient.Config;

public class AppSettings
{
    public AppRunMode RunMode { get; set; } = AppRunMode.LocalSqlite;

    public string SqliteConnection { get; set; } = AppConfigService.CreateSqliteConnection(AppConfigService.GetDefaultSqliteDatabasePath());

    public string PostgreSqlConnection { get; set; } = "Host=127.0.0.1;Port=5432;Username=postgres;Database=label_print;";

    public string LocalTemplateFolder { get; set; } = AppConfigService.GetDefaultTemplateFolder();

    public string OperatorName { get; set; } = "admin";

    public string? DefaultPrinterName { get; set; }

    public int DefaultPrintCopies { get; set; } = 1;

    public bool ConfirmBeforePrint { get; set; } = true;

    public bool EnableSqlLogging { get; set; } = false;

    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    public AppLanguage Language { get; set; } = AppLanguage.ZhCn;

    public LicenseMode LicenseMode { get; set; } = LicenseMode.Standalone;

    public string ProductCode { get; set; } = "LABEL_PRINT_CLIENT";

    public string LicenseServerUrl { get; set; } = "https://127.0.0.1:5001/";

    public string LicenseAccessKey { get; set; } = string.Empty;

    public string StandaloneLicenseFilePath { get; set; } = AppConfigService.GetDefaultStandaloneLicenseFilePath();

    public int LicenseHeartbeatIntervalSeconds { get; set; } = 30;

    public int LicenseHeartbeatTimeoutSeconds { get; set; } = 120;

    public string UpdateServerUrl { get; set; } = "https://127.0.0.1:5001/";

    public string UpdateChannel { get; set; } = "stable";

    public bool AutoCheckUpdates { get; set; } = true;
}
