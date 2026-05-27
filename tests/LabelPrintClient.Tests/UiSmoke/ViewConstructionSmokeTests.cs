using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Views;
using LabelPrintClient.Modules.PrintHistory.Views;
using LabelPrintClient.Modules.Settings.Views;
using LabelPrintClient.Modules.TaskCenter.Views;
using LabelPrintClient.Modules.Template.Views;
using LabelPrintClient.Tests.Infrastructure;
using System.Windows;

namespace LabelPrintClient.Tests.UiSmoke;

public class ViewConstructionSmokeTests
{
    [Fact]
    [Trait("Category", "UiSmoke")]
    public async Task ModuleViewsAndEditWindows_CanBeConstructedOnStaThread()
    {
        using var database = TestDatabase.Create();

        await StaThreadRunner.RunAsync(() =>
        {
            EnsureApplicationResources();
            var views = new object[]
            {
                new TemplateManageView(),
                new PrintCenterView(),
                new PrintHistoryView(),
                new TaskCenterView(),
                new SettingsView()
            };

            Assert.All(views, Assert.NotNull);

            var windows = new Window[]
            {
                new CategoryEditWindow(),
                new FieldEditWindow(),
                new TemplateEditWindow(),
                new BatchNoInputWindow()
            };

            Assert.All(windows, Assert.NotNull);
            foreach (var window in windows)
                window.Close();

            Application.Current?.Shutdown();
        });

        AppDb.Close();
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        if (application.Resources.MergedDictionaries.Count > 0)
            return;

        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml", UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml", UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/LabelPrintClient;component/Themes/ModernStyle.xaml", UriKind.Absolute)
        });
    }
}
