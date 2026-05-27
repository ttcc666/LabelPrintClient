using System.Windows;
using LabelPrintClient.Modules.Auth.Services;

namespace LabelPrintClient.Modules.Auth.Infrastructure;

public static class PermissionAssist
{
    public static readonly DependencyProperty PermissionKeyProperty =
        DependencyProperty.RegisterAttached(
            "PermissionKey",
            typeof(string),
            typeof(PermissionAssist),
            new PropertyMetadata(null, OnPermissionKeyChanged));

    public static string? GetPermissionKey(DependencyObject obj) =>
        (string?)obj.GetValue(PermissionKeyProperty);

    public static void SetPermissionKey(DependencyObject obj, string? value) =>
        obj.SetValue(PermissionKeyProperty, value);

    private static void OnPermissionKeyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not FrameworkElement element)
            return;

        element.Loaded -= Element_Loaded;
        element.Loaded += Element_Loaded;
        CurrentUserService.CurrentUserChanged -= CurrentUserService_CurrentUserChanged;
        CurrentUserService.CurrentUserChanged += CurrentUserService_CurrentUserChanged;
        Apply(element);
    }

    private static void Element_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            Apply(element);
    }

    private static void CurrentUserService_CurrentUserChanged(object? sender, EventArgs e)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
            return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => CurrentUserService_CurrentUserChanged(sender, e));
            return;
        }

        foreach (Window window in app.Windows)
            ApplyRecursive(window);
    }

    private static void ApplyRecursive(DependencyObject node)
    {
        if (node is FrameworkElement element)
            Apply(element);

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            ApplyRecursive(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
    }

    public static void Apply(FrameworkElement element)
    {
        var permissionKey = GetPermissionKey(element);
        if (string.IsNullOrWhiteSpace(permissionKey))
            return;

        element.Visibility = CurrentUserService.HasPermission(permissionKey)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
