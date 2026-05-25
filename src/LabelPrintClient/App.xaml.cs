using System.IO;
using System.Windows;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Services;
using LabelPrintClient.Infrastructure;
using Stimulsoft.Report;

namespace LabelPrintClient;

public partial class App : System.Windows.Application
{
    private const string StimulsoftLocalizationFileName = "zh-CHS.xml";

    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // 尽早挂载全局未处理异常捕获，确保启动阶段其他异常也能被记录
        RegisterGlobalExceptionHandlers();

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
            AppLogger.LogError("系统初始化失败", ex);
            AppMessageBox.Show($"系统初始化失败：{ex.Message}\n\n详情请查看应用程序根目录下 logs 文件夹中的日志文件。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
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
            $"程序运行中发生异常：\n{e.Exception.Message}\n\n系统已尝试拦截此错误，详情请查看本地 logs 文件夹中的日志。\n\n是否尝试忽略此错误并继续运行？",
            "系统运行异常",
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
                                $"程序遭遇严重致命错误，即将关闭。\n错误信息：{(ex != null ? ex.Message : "未知异常")}\n\n详情请查看本地 logs 文件夹下的日志文件。",
                                "系统致命错误",
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
                            $"程序遭遇严重致命错误，即将关闭。\n错误信息：{(ex != null ? ex.Message : "未知异常")}\n\n详情请查看本地 logs 文件夹下的日志文件。",
                            "系统致命错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch
                    {
                        // 兜底降级使用系统原生弹窗
                        System.Windows.MessageBox.Show(
                            $"程序遭遇严重致命错误，即将关闭。\n错误信息：{(ex != null ? ex.Message : "未知异常")}\n\n详情请查看本地 logs 文件夹下的日志文件。",
                            "系统致命错误",
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
                $"程序遭遇严重致命错误，即将关闭。\n错误信息：{(ex != null ? ex.Message : "未知异常")}\n\n详情请查看本地 logs 文件夹下的日志文件。",
                "系统致命错误",
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