using LabelPrintClient.Config;
using LabelPrintClient.Modules.Auth.Infrastructure;
using LabelPrintClient.Modules.Auth.Services;
using LabelPrintClient.Modules.Auth.Views;
using LabelPrintClient.Modules.PrintCenter.Views;
using LabelPrintClient.Modules.PrintHistory.Views;
using LabelPrintClient.Modules.Settings.Views;
using LabelPrintClient.Modules.TaskCenter.Views;
using LabelPrintClient.Modules.Template.Views;
using LabelPrintClient.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelPrintClient;

public partial class MainWindow : HandyControl.Controls.Window
{
    private readonly PrintCenterView _printCenterView = new();
    private readonly PrintHistoryView _printHistoryView = new();
    private readonly TaskCenterView _taskCenterView = new();
    private readonly TemplateManageView _templateManageView = new();
    private readonly SettingsView _settingsView = new();
    private readonly AccountPermissionView _accountPermissionView = new();
    private bool _isSelectingNavigation;
    private bool _isUpdatingSelector;

    public MainWindow()
    {
        InitializeComponent();
        InitializeThemeSelector();
        InitializeLanguageSelector();
        UpdateCurrentUserText();
        AppThemeService.ThemeModeChanged += AppThemeService_ThemeModeChanged;
        AppLanguageService.LanguageChanged += AppLanguageService_LanguageChanged;
        Loaded += (_, _) =>
        {
            HandyControl.Controls.Growl.Register(AppMessageBox.ToastToken, ToastHost);
            SelectFirstAllowedNavigation();
        };
        Closed += (_, _) =>
        {
            HandyControl.Controls.Growl.Unregister(AppMessageBox.ToastToken, ToastHost);
            AppThemeService.ThemeModeChanged -= AppThemeService_ThemeModeChanged;
            AppLanguageService.LanguageChanged -= AppLanguageService_LanguageChanged;
        };
    }

    private async void RootNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectingNavigation)
            return;

        if (RootNavigation.SelectedItem is not ListBoxItem clickedItem) return;
        if (!HasNavigationPermission(clickedItem))
        {
            SelectFirstAllowedNavigation();
            return;
        }

        await OpenNavigationItemAsync(clickedItem);
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        AuthService.Logout();
        var loginWindow = new LoginWindow
        {
            Owner = this
        };

        Hide();
        if (loginWindow.ShowDialog() == true)
        {
            UpdateCurrentUserText();
            SelectFirstAllowedNavigation();
            Show();
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }

    private void ToastHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        HandyControl.Controls.Growl.Clear(AppMessageBox.ToastToken);
        e.Handled = true;
    }

    private void UpdateCurrentUserText()
    {
        var current = CurrentUserService.Current;
        CurrentUserText.Text = current == null
            ? string.Empty
            : $"{current.OperatorName} ({current.UserName})";
    }

    private void SelectFirstAllowedNavigation()
    {
        var items = RootNavigation.Items.OfType<ListBoxItem>().ToList();
        foreach (var item in items)
            ApplyNavigationPermission(item);

        var selected = RootNavigation.SelectedItem as ListBoxItem;
        if (selected != null && selected.Visibility == Visibility.Visible && HasNavigationPermission(selected))
            return;

        var firstVisible = items.FirstOrDefault(x => x.Visibility == Visibility.Visible && HasNavigationPermission(x));
        if (firstVisible != null)
        {
            _isSelectingNavigation = true;
            RootNavigation.SelectedItem = firstVisible;
            _isSelectingNavigation = false;
            _ = OpenNavigationItemAsync(firstVisible);
            return;
        }

        _isSelectingNavigation = true;
        RootNavigation.SelectedItem = null;
        _isSelectingNavigation = false;
        WorkspaceContent.Content = BuildNoPermissionContent();
    }

    private async Task OpenNavigationItemAsync(ListBoxItem clickedItem)
    {
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

            case "AccountPermission":
                WorkspaceContent.Content = _accountPermissionView;
                break;

            default:
                WorkspaceContent.Content = _printCenterView;
                break;
        }
    }

    private static bool HasNavigationPermission(ListBoxItem item)
    {
        var permissionKey = PermissionAssist.GetPermissionKey(item);
        return string.IsNullOrWhiteSpace(permissionKey) || CurrentUserService.HasPermission(permissionKey);
    }

    private static void ApplyNavigationPermission(ListBoxItem item)
    {
        item.Visibility = HasNavigationPermission(item) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static UIElement BuildNoPermissionContent()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = AppLanguageService.GetString("Nav.NoPermissionTitle"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(new TextBlock
        {
            Text = AppLanguageService.GetString("Nav.NoPermissionHint"),
            FontSize = 13,
            Foreground = System.Windows.Media.Brushes.Gray,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        });

        var grid = new Grid();
        grid.Children.Add(panel);
        return grid;
    }

    private void InitializeThemeSelector()
    {
        SelectThemeModeInComboBox(App.Settings.ThemeMode);
    }

    private void AppThemeService_ThemeModeChanged(object? sender, AppThemeMode themeMode)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SelectThemeModeInComboBox(themeMode));
            return;
        }

        SelectThemeModeInComboBox(themeMode);
    }

    private void SelectThemeModeInComboBox(AppThemeMode themeMode)
    {
        if (_isUpdatingSelector)
            return;

        _isUpdatingSelector = true;
        try
        {
            var targetTag = themeMode.ToString();
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == targetTag)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _isUpdatingSelector = false;
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelector ||
            ThemeComboBox.SelectedItem is not ComboBoxItem selectedItem ||
            !Enum.TryParse<AppThemeMode>(selectedItem.Tag?.ToString(), out var themeMode))
        {
            return;
        }

        if (themeMode == App.Settings.ThemeMode)
        {
            return;
        }

        var previousMode = App.Settings.ThemeMode;
        if (AppThemeService.TryApplyAndPersist(themeMode, out var errorMessage))
        {
            return;
        }

        SelectThemeModeInComboBox(previousMode);
        AppMessageBox.Show(
            AppLanguageService.Format("Theme.SwitchFailed", errorMessage),
            AppLanguageService.GetString("Theme.SwitchTitle"),
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    private void InitializeLanguageSelector()
    {
        SelectLanguageInComboBox(App.Settings.Language);
    }

    private void AppLanguageService_LanguageChanged(object? sender, AppLanguage language)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SelectLanguageInComboBox(language));
            return;
        }

        SelectLanguageInComboBox(language);
    }

    private void SelectLanguageInComboBox(AppLanguage language)
    {
        if (_isUpdatingSelector)
            return;

        _isUpdatingSelector = true;
        try
        {
            var targetTag = language == AppLanguage.ZhCn ? "ZhCn" : "EnUs";
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == targetTag)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _isUpdatingSelector = false;
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelector ||
            LanguageComboBox.SelectedItem is not ComboBoxItem selectedItem ||
            selectedItem.Tag?.ToString() is not string tag)
        {
            return;
        }

        AppLanguage language = tag switch
        {
            "ZhCn" => AppLanguage.ZhCn,
            "EnUs" => AppLanguage.EnUs,
            _ => App.Settings.Language
        };

        if (language == App.Settings.Language)
        {
            return;
        }

        var previousLanguage = App.Settings.Language;
        if (AppLanguageService.TryApplyAndPersist(language, out var errorMessage))
        {
            return;
        }

        SelectLanguageInComboBox(previousLanguage);
        AppMessageBox.Show(
            AppLanguageService.Format("Language.SwitchFailed", errorMessage),
            AppLanguageService.GetString("Settings.LanguageSwitch"),
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }
}
