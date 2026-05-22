using LabelPrintClient.Config;
using System.Windows;
using HandyControl.Themes;
using HandyControl.Data;

namespace LabelPrintClient.Services;

public static class AppThemeService
{
    private const string ModernStyleResource = "pack://application:,,,/LabelPrintClient;component/Themes/ModernStyle.xaml";

    public static event EventHandler<AppThemeMode>? ThemeModeChanged;

    public static void Apply(AppThemeMode mode)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplyInternal(mode));
            return;
        }

        ApplyInternal(mode);
    }

    public static bool TryApply(AppThemeMode mode, out string errorMessage)
    {
        try
        {
            Apply(mode);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryApplyAndSetRuntime(AppThemeMode mode, out string errorMessage)
    {
        var previousMode = App.Settings.ThemeMode;

        try
        {
            Apply(mode);
            App.Settings.ThemeMode = mode;
            ThemeModeChanged?.Invoke(null, mode);

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            TryApply(previousMode, out _);
            return false;
        }
    }

    public static bool TryApplyAndPersist(AppThemeMode mode, out string errorMessage)
    {
        var previousMode = App.Settings.ThemeMode;

        try
        {
            Apply(mode);

            var settings = AppConfigService.LoadOrCreateDefault();
            settings.ThemeMode = mode;
            AppConfigService.Save(settings);

            App.Settings.ThemeMode = mode;
            ThemeModeChanged?.Invoke(null, mode);

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            TryApply(previousMode, out _);
            return false;
        }
    }

    private static void ApplyInternal(AppThemeMode mode)
    {
        try
        {
            var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
            dicts.Clear();
            string skinStr = mode == AppThemeMode.Dark ? "SkinDark" : "SkinDefault";
            dicts.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/HandyControl;component/Themes/{skinStr}.xaml", UriKind.Absolute) });
            dicts.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml", UriKind.Absolute) });
            dicts.Add(new ResourceDictionary { Source = new Uri(ModernStyleResource, UriKind.Absolute) });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplyTheme Error: {ex}");
            throw;
        }
    }
}

