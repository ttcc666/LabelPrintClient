using System.IO;
using System.Windows;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Services;
using Stimulsoft.Report;

namespace LabelPrintClient;

public partial class App : System.Windows.Application
{
    private const string StimulsoftLocalizationFileName = "zh-CHS.xml";

    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            LoadStimulsoftLocalization();
            Settings = AppConfigService.LoadOrCreateDefault();
            AppThemeService.Apply(Settings.ThemeMode);
            AppDb.Init(Settings);
            DbInitializer.InitTables();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"系统初始化失败：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void LoadStimulsoftLocalization()
    {
        var localizationDirectory = Path.Combine(AppContext.BaseDirectory, "Localization");
        var localizationFile = Path.Combine(localizationDirectory, StimulsoftLocalizationFileName);

        StiOptions.Configuration.DirectoryLocalization = localizationDirectory;
        StiOptions.Configuration.Localization = StimulsoftLocalizationFileName;

        if (File.Exists(localizationFile))
            StiOptions.Localization.Load(localizationFile);
    }
}