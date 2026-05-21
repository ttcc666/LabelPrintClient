using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Views;
using Wpf.Ui.Controls;

namespace LabelPrintClient;

public partial class MainWindow : FluentWindow
{
    private readonly PrintCenterView _printCenterView = new();
    private readonly PrintHistoryView _printHistoryView = new();
    private readonly TemplateManageView _templateManageView = new();

    public MainWindow()
    {
        InitializeComponent();
        WorkspaceContent.Content = _printCenterView;
        Loaded += (_, _) =>
        {
            if (RootNavigation.MenuItems.Count > 0 && RootNavigation.MenuItems[0] is NavigationViewItem firstItem)
            {
                firstItem.IsActive = true;
            }
        };
    }

    private void NavItem_Click(object sender, RoutedEventArgs e)
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
                _printHistoryView.RefreshHistory();
                break;
            case "TemplateManage":
                WorkspaceContent.Content = _templateManageView;
                break;
            default:
                WorkspaceContent.Content = _printCenterView;
                break;
        }
    }
}
