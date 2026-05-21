using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.PrintCenter.Views;
using LabelPrintClient.Modules.Template.Views;
using LabelPrintClient.Modules.PrintHistory.Views;
using LabelPrintClient.Modules.TaskCenter.Views;
using LabelPrintClient.Modules.Settings.Views;
using LabelPrintClient.Services;
using Wpf.Ui.Controls;

namespace LabelPrintClient;

public partial class MainWindow : FluentWindow
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
            if (RootNavigation.MenuItems.Count > 0 && RootNavigation.MenuItems[0] is NavigationViewItem firstItem)
            {
                firstItem.IsActive = true;
            }
        };
        Closed += (_, _) => AppThemeService.ThemeModeChanged -= AppThemeService_ThemeModeChanged;
    }

    private async void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationViewItem clickedItem) return;

        foreach (var menuItem in RootNavigation.MenuItems)
        {
            if (menuItem is NavigationViewItem item)
            {
                item.IsActive = (item == clickedItem);
            }
        }

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
        System.Windows.MessageBox.Show($"主题切换失败：{errorMessage}", "主题切换", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
        SystemThemeButton.Appearance = themeMode == AppThemeMode.System ? ControlAppearance.Primary : ControlAppearance.Transparent;
        LightThemeButton.Appearance = themeMode == AppThemeMode.Light ? ControlAppearance.Primary : ControlAppearance.Transparent;
        DarkThemeButton.Appearance = themeMode == AppThemeMode.Dark ? ControlAppearance.Primary : ControlAppearance.Transparent;
    }
}
