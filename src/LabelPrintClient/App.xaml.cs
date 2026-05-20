using System.Windows;
using LabelPrintClient.Config;
using LabelPrintClient.Database;

namespace LabelPrintClient;

public partial class App : System.Windows.Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Settings = AppConfigService.LoadOrCreateDefault();
            AppDb.Init(Settings);
            DbInitializer.InitTables();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"系统初始化失败：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
