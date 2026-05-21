using LabelPrintClient.Config;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LabelPrintClient.Services;

public static class AppThemeService
{
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

    public static ApplicationTheme ResolveTheme(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Light => ApplicationTheme.Light,
            AppThemeMode.Dark => ApplicationTheme.Dark,
            _ => ResolveSystemTheme()
        };
    }

    private static ApplicationTheme ResolveSystemTheme()
    {
        try
        {
            return ApplicationThemeManager.GetSystemTheme() switch
            {
                SystemTheme.Dark or
                SystemTheme.HCBlack or
                SystemTheme.HC1 or
                SystemTheme.HC2 or
                SystemTheme.Glow or
                SystemTheme.CapturedMotion => ApplicationTheme.Dark,
                _ => ApplicationTheme.Light
            };
        }
        catch
        {
            return ApplicationTheme.Light;
        }
    }

    private static void ApplyInternal(AppThemeMode mode)
    {
        var theme = ResolveTheme(mode);

        try
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
        }
        catch
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.None, true);
        }
    }
}
