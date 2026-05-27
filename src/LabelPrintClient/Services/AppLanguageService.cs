using System.Globalization;
using System.Windows;
using LabelPrintClient.Config;

namespace LabelPrintClient.Services;

public static class AppLanguageService
{
    private const string FallbackResource = "pack://application:,,,/LabelPrintClient;component/Resources/Strings.zh-CN.xaml";
    private const string EnglishResource = "pack://application:,,,/LabelPrintClient;component/Resources/Strings.en-US.xaml";
    private const string LanguageResourceMarker = "/Resources/Strings.";

    public static event EventHandler<AppLanguage>? LanguageChanged;

    public static void Apply(AppLanguage language)
    {
        ApplyResources(language);
        CultureInfo.CurrentUICulture = ToCulture(language);
        CultureInfo.CurrentCulture = ToCulture(language);
    }

    public static bool TryApplyAndSetRuntime(AppLanguage language, out string errorMessage)
    {
        var previous = App.Settings.Language;
        try
        {
            Apply(language);
            App.Settings.Language = language;
            LanguageChanged?.Invoke(null, language);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                Apply(previous);
                App.Settings.Language = previous;
            }
            catch
            {
            }

            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryApplyAndPersist(AppLanguage language, out string errorMessage)
    {
        var previous = App.Settings.Language;
        try
        {
            Apply(language);
            var settings = AppConfigService.LoadOrCreateDefault();
            settings.Language = language;
            AppConfigService.Save(settings);
            App.Settings.Language = language;
            LanguageChanged?.Invoke(null, language);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                Apply(previous);
                App.Settings.Language = previous;
            }
            catch
            {
            }

            errorMessage = ex.Message;
            return false;
        }
    }

    public static string GetString(string key)
    {
        return TryFindString(key) ?? key;
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetString(key), args);
    }

    internal static void ApplyResources(AppLanguage language)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.ToString();
            if (source != null && source.Contains(LanguageResourceMarker, StringComparison.OrdinalIgnoreCase))
                dictionaries.RemoveAt(i);
        }

        dictionaries.Add(new ResourceDictionary { Source = new Uri(FallbackResource, UriKind.Absolute) });
        if (language == AppLanguage.EnUs)
            dictionaries.Add(new ResourceDictionary { Source = new Uri(EnglishResource, UriKind.Absolute) });
    }

    private static string? TryFindString(string key)
    {
        var app = System.Windows.Application.Current;
        if (app?.Resources.Contains(key) == true)
            return app.Resources[key]?.ToString();

        try
        {
            var fallback = new ResourceDictionary { Source = new Uri(FallbackResource, UriKind.Absolute) };
            return fallback.Contains(key) ? fallback[key]?.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static CultureInfo ToCulture(AppLanguage language)
    {
        return language == AppLanguage.EnUs
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("zh-CN");
    }
}
