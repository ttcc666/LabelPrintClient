using System.Windows;

namespace LabelPrintClient.Services;

public static class AppMessageBox
{
    public const string ToastToken = "AppToast";

    public static MessageBoxResult Show(
        string messageBoxText,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => ShowCore(messageBoxText, caption, button, icon, defaultResult));
        }

        return ShowCore(messageBoxText, caption, button, icon, defaultResult);
    }

    public static bool Confirm(
        string messageBoxText,
        string caption = "确认",
        MessageBoxImage icon = MessageBoxImage.Warning)
    {
        return Show(messageBoxText, caption, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes;
    }

    public static MessageBoxResult Info(string messageBoxText, string caption = "提示")
    {
        return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static MessageBoxResult Success(string messageBoxText, string caption = "成功")
    {
        return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static MessageBoxResult Warning(string messageBoxText, string caption = "提示")
    {
        return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static MessageBoxResult Error(string messageBoxText, string caption = "错误")
    {
        return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static MessageBoxResult ShowCore(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        MessageBoxResult defaultResult)
    {
        // 对于只需点击确定、仅作提示用途的消息，使用更加美观和非侵入式的 Growl (Toast) 弹出框
        if (button == MessageBoxButton.OK)
        {
            var isSuccess = caption.Contains("成功") || messageBoxText.Contains("成功");

            if (icon == MessageBoxImage.Error)
            {
                HandyControl.Controls.Growl.Error(messageBoxText, ToastToken);
                return MessageBoxResult.OK;
            }
            if (icon == MessageBoxImage.Warning)
            {
                HandyControl.Controls.Growl.Warning(messageBoxText, ToastToken);
                return MessageBoxResult.OK;
            }
            if (icon == MessageBoxImage.Information)
            {
                if (isSuccess)
                    HandyControl.Controls.Growl.Success(messageBoxText, ToastToken);
                else
                    HandyControl.Controls.Growl.Info(messageBoxText, ToastToken);
                return MessageBoxResult.OK;
            }

            // 默认根据内容推断
            if (isSuccess)
                HandyControl.Controls.Growl.Success(messageBoxText, ToastToken);
            else if (caption.Contains("错误") || messageBoxText.Contains("失败"))
                HandyControl.Controls.Growl.Error(messageBoxText, ToastToken);
            else if (caption.Contains("警告"))
                HandyControl.Controls.Growl.Warning(messageBoxText, ToastToken);
            else
                HandyControl.Controls.Growl.Info(messageBoxText, ToastToken);

            return MessageBoxResult.OK;
        }

        var owner = FindOwner();
        if (owner == null)
        {
            return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
        }

        try
        {
            return HandyControl.Controls.MessageBox.Show(owner, messageBoxText, caption, button, icon, defaultResult);
        }
        catch
        {
            return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
        }
    }

    private static System.Windows.Window? FindOwner()
    {
        var app = System.Windows.Application.Current;
        if (app == null)
            return null;

        var windows = app.Windows.OfType<System.Windows.Window>().Where(x => x.IsVisible).ToList();
        return windows.FirstOrDefault(x => x.IsActive)
            ?? (app.MainWindow?.IsVisible == true ? app.MainWindow : null)
            ?? windows.FirstOrDefault();
    }
}
