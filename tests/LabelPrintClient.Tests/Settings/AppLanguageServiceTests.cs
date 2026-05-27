using System.Windows;
using LabelPrintClient.Config;
using LabelPrintClient.Services;
using LabelPrintClient.Infrastructure;

namespace LabelPrintClient.Tests.Settings;

public class AppLanguageServiceTests
{
    [Fact]
    [Trait("Category", "UiSmoke")]
    public async Task Apply_SwitchesLanguageResourcesWithoutDuplicates()
    {
        await StaThreadRunner.RunAsync(() =>
        {
            var application = Application.Current ?? new Application();
            application.Resources.MergedDictionaries.Clear();

            AppLanguageService.Apply(AppLanguage.ZhCn);
            AppLanguageService.Apply(AppLanguage.EnUs);
            AppLanguageService.Apply(AppLanguage.EnUs);

            Assert.Equal("Label Print Client", AppLanguageService.GetString("App.Title"));
            Assert.Equal("Missing.Key", AppLanguageService.GetString("Missing.Key"));
            Assert.Equal(2, application.Resources.MergedDictionaries.Count(x =>
                x.Source?.ToString().Contains("/Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true));

            Application.Current?.Shutdown();
        });
    }
}
