using LabelPrintClient.Config;
using LabelPrintClient.Modules.PrintCenter.Views;
using LabelPrintClient.Modules.PrintHistory.Views;
using LabelPrintClient.Modules.Settings.Views;
using LabelPrintClient.Modules.TaskCenter.Views;
using LabelPrintClient.Modules.Template.Views;
using LabelPrintClient.Services;
using System.Windows;
using System.Windows.Controls;

namespace LabelPrintClient;

public partial class MainWindow : HandyControl.Controls.Window
{
    private readonly PrintCenterView _printCenterView = new();
    private readonly PrintHistoryView _printHistoryView = new();
    private readonly TaskCenterView _taskCenterView = new();
    private readonly TemplateManageView _templateManageView = new();
    private readonly SettingsView _settingsView = new();

    public MainWindow()
    {
        InitializeComponent();
        InitializeThemeSelector();
        AppThemeService.ThemeModeChanged += AppThemeService_ThemeModeChanged;
        WorkspaceContent.Content = _printCenterView;
        Loaded += (_, _) =>
        {
            if (RootNavigation.Items.Count > 0)
            {
                RootNavigation.SelectedIndex = 0;
            }
        };
        Closed += (_, _) => AppThemeService.ThemeModeChanged -= AppThemeService_ThemeModeChanged;
    }

    private async void RootNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RootNavigation.SelectedItem is not ListBoxItem clickedItem) return;

        switch (clickedItem.Tag?.ToString())
        {
            case "PrintHistory":
                WorkspaceContent.Content = _printHistoryView;
                await _printHistoryView.RefreshHistoryAsync();
                break;

            case "TaskCenter":
                WorkspaceContent.Content = _taskCenterView;
                break;

            case "TemplateManage":
                WorkspaceContent.Content = _templateManageView;
                break;

            case "Settings":
                WorkspaceContent.Content = _settingsView;
                break;

            default:
                WorkspaceContent.Content = _printCenterView;
                break;
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            !Enum.TryParse<AppThemeMode>(button.Tag?.ToString(), out var themeMode) ||
            themeMode == App.Settings.ThemeMode)
        {
            return;
        }

        var previousMode = App.Settings.ThemeMode;
        if (AppThemeService.TryApplyAndPersist(themeMode, out var errorMessage))
            return;

        SelectThemeMode(previousMode);
        AppMessageBox.Show($"主题切换失败：{errorMessage}", "主题切换", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    private void AppThemeService_ThemeModeChanged(object? sender, AppThemeMode themeMode)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SelectThemeMode(themeMode));
            return;
        }

        SelectThemeMode(themeMode);
    }

    private void InitializeThemeSelector()
    {
        SelectThemeMode(App.Settings.ThemeMode);
    }

    private void SelectThemeMode(AppThemeMode themeMode)
    {
        var primaryBrush = FindResource("PrimaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue;
        var transparentBrush = System.Windows.Media.Brushes.Transparent;
        var whiteText = System.Windows.Media.Brushes.White;
        var primaryText = FindResource("PrimaryTextBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;

        SystemThemeButton.Background = themeMode == AppThemeMode.System ? primaryBrush : transparentBrush;
        SystemThemeButton.Foreground = themeMode == AppThemeMode.System ? whiteText : primaryText;

        LightThemeButton.Background = themeMode == AppThemeMode.Light ? primaryBrush : transparentBrush;
        LightThemeButton.Foreground = themeMode == AppThemeMode.Light ? whiteText : primaryText;

        DarkThemeButton.Background = themeMode == AppThemeMode.Dark ? primaryBrush : transparentBrush;
        DarkThemeButton.Foreground = themeMode == AppThemeMode.Dark ? whiteText : primaryText;
    }
}