using System.IO;
using System.Windows;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Modules.Auth.Services;
using LabelPrintClient.Modules.Auth.Views;
using LabelPrintClient.Services;
using LabelPrintClient.Infrastructure;
using Stimulsoft.Report;

namespace LabelPrintClient;

public partial class App : System.Windows.Application
{
    private const string StimulsoftLocalizationFileName = "zh-CHS.xml";

    public static AppSettings Settings { get; private set; } = new();

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        // 尽早挂载全局未处理异常捕获，确保启动阶段其他异常也能被记录
        RegisterGlobalExceptionHandlers();

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            AppLogger.LogInfo("启动初始化开始");
            LoadStimulsoftLocalization();
            Settings = AppConfigService.LoadOrCreateDefault();
            AppLogger.LogInfo($"配置已加载，运行模式：{Settings.RunMode}");
            AppLogger.LogInfo("开始应用主题");
            AppThemeService.Apply(Settings.ThemeMode);
            AppLogger.LogInfo("主题应用完成");
            AppLogger.LogInfo("开始应用语言");
            AppLanguageService.Apply(Settings.Language);
            AppLogger.LogInfo($"语言应用完成：{Settings.Language}");
            AppLogger.LogInfo("开始初始化数据库连接");
            AppDb.Init(Settings);
            AppLogger.LogInfo("数据库连接对象初始化完成");
            AppLogger.LogInfo("开始初始化数据库表");
            DbInitializer.InitTables();
            AppLogger.LogInfo("业务表与 RBAC 表初始化完成");
            AppLogger.LogInfo("开始同步权限资源");
            await PermissionBootstrapper.SyncAsync();
            AppLogger.LogInfo("数据库表与权限资源初始化完成");

            if (!await ShowAuthFlowAsync())
            {
                AppLogger.LogInfo("用户取消登录，应用退出");
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            AppLogger.LogInfo("主窗口已显示");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("系统初始化失败", ex);
            AppMessageBox.Show(
                AppLanguageService.Format("App.StartupFailed", ex.Message),
                AppLanguageService.GetString("App.StartupFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static async Task<bool> ShowAuthFlowAsync()
    {
        var hasAdministratorUser = await PermissionBootstrapper.HasAdministratorUserAsync();
        if (!hasAdministratorUser)
        {
            var initialAdminWindow = new InitialAdminWindow();
            return initialAdminWindow.ShowDialog() == true;
        }

        var loginWindow = new LoginWindow();
        return loginWindow.ShowDialog() == true;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // 1. UI 线程未处理异常
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // 2. 非 UI 线程未处理异常 (例如后台工作线程抛出且未捕获的异常)
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // 3. Task 任务中未观测的异常 (例如 Task 抛出异常但没有被 await 或访问 Result)
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // 记录错误日志
        AppLogger.LogError("UI线程未处理异常", e.Exception);

        // 调用自定义 AppMessageBox.Show 并选择 MessageBoxButton.YesNo。
        // 由于 YesNo 按钮不会进入 button == MessageBoxButton.OK 分支，因此它会完美唤起 HandyControl 高颜值扁平化的模态实体弹窗，而不是右上角的消息气泡（Growl/Toast）！
        var result = AppMessageBox.Show(
            AppLanguageService.Format("App.RuntimeException", e.Exception.Message),
            AppLanguageService.GetString("App.RuntimeExceptionTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            // 标记为已处理，拦截异常，防止程序崩溃退出
            e.Handled = true;
        }
        else
        {
            // 用户选择退出，则直接优雅关闭应用
            Shutdown();
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        AppLogger.LogError($"非UI线程致命未处理异常 (IsTerminating: {e.IsTerminating})", ex);

        // 由于后台线程严重异常是在 MTA 线程触发的，直接弹窗可能会因为线程不是 STA 模型而二次崩溃。
        // 我们通过主 UI 线程的 Dispatcher 封送弹窗逻辑。
        // 为了确保弹窗的扁平化精美效果，并避开 AppMessageBox.Show 针对 OK 按钮转化成 Growl 气泡的局限，我们直接显式寻找活动窗口并调用原生的 HandyControl.Controls.MessageBox.Show！
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            try
            {
                dispatcher.Invoke(() =>
                {
                    var owner = System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>().FirstOrDefault(x => x.IsActive)
                        ?? System.Windows.Application.Current?.MainWindow;
                    
                    if (owner != null && owner.IsVisible)
                    {
                        try
                        {
                            HandyControl.Controls.MessageBox.Show(
                                owner,
                                AppLanguageService.Format("App.FatalError", ex != null ? ex.Message : AppLanguageService.GetString("App.UnknownException")),
                                AppLanguageService.GetString("App.FatalErrorTitle"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            return;
                        }
                        catch { }
                    }

                    // 降级使用 HandyControl 无 owner 的弹窗
                    try
                    {
                        HandyControl.Controls.MessageBox.Show(
                            AppLanguageService.Format("App.FatalError", ex != null ? ex.Message : AppLanguageService.GetString("App.UnknownException")),
                            AppLanguageService.GetString("App.FatalErrorTitle"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch
                    {
                        // 兜底降级使用系统原生弹窗
                        System.Windows.MessageBox.Show(
                            AppLanguageService.Format("App.FatalError", ex != null ? ex.Message : AppLanguageService.GetString("App.UnknownException")),
                            AppLanguageService.GetString("App.FatalErrorTitle"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                });
                return;
            }
            catch
            {
                // Dispatcher 封送失败时，降级进入下方直接弹窗逻辑
            }
        }

        try
        {
            System.Windows.MessageBox.Show(
                AppLanguageService.Format("App.FatalError", ex != null ? ex.Message : AppLanguageService.GetString("App.UnknownException")),
                AppLanguageService.GetString("App.FatalErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // 如果连直接弹窗也失败，静默退出，防止死锁
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 记录未观测的 Task 异常
        AppLogger.LogError("异步任务(Task)未观测异常", e.Exception);

        // 标记为已观测，防止可能导致程序退出（取决于 .NET 版本行为）
        e.SetObserved();
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
