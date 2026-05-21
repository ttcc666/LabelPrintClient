using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Views;
using Wpf.Ui.Controls;

namespace LabelPrintClient;

public partial class MainWindow : FluentWindow
{
    private readonly PrintCenterView _printCenterView = new();
    private readonly TemplateManageView _templateManageView = new();

    public MainWindow()
    {
        InitializeComponent();
        NavigationList.SelectedIndex = 0;
        WorkspaceContent.Content = _printCenterView;
    }

    private void NavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavigationList.SelectedItem is not ListBoxItem item) return;

        WorkspaceContent.Content = item.Tag?.ToString() switch
        {
            "TemplateManage" => _templateManageView,
            _ => _printCenterView
        };
    }
}
