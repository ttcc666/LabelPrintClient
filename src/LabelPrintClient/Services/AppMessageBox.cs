using System.Windows;
using HandyControl.Data;

namespace LabelPrintClient.Services;

public static class AppMessageBox
{
    public const string ToastToken = "AppToast";
    private const int ToastWaitTimeSeconds = 5;

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
        string? caption = null,
        MessageBoxImage icon = MessageBoxImage.Warning)
    {
        var title = caption ?? AppLanguageService.GetString("Common.Confirm");
        return Show(messageBoxText, title, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes;
    }

    public static MessageBoxResult Info(string messageBoxText, string? caption = null)
    {
        var title = caption ?? AppLanguageService.GetString("Common.Prompt");
        return Show(messageBoxText, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static MessageBoxResult Success(string messageBoxText, string? caption = null)
    {
        var title = caption ?? AppLanguageService.GetString("Common.Success");
        return Show(messageBoxText, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static MessageBoxResult Warning(string messageBoxText, string? caption = null)
    {
        var title = caption ?? AppLanguageService.GetString("Common.Warning");
        return Show(messageBoxText, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static MessageBoxResult Error(string messageBoxText, string? caption = null)
    {
        var title = caption ?? AppLanguageService.GetString("Common.Error");
        return Show(messageBoxText, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
            var isSuccess = caption.Contains("成功") || messageBoxText.Contains("成功") ||
                            caption.IndexOf("Success", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            messageBoxText.IndexOf("Success", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (icon == MessageBoxImage.Error)
            {
                HandyControl.Controls.Growl.Error(CreateGrowlInfo(messageBoxText, InfoType.Error));
                return MessageBoxResult.OK;
            }
            if (icon == MessageBoxImage.Warning)
            {
                HandyControl.Controls.Growl.Warning(CreateGrowlInfo(messageBoxText, InfoType.Warning));
                return MessageBoxResult.OK;
            }
            if (icon == MessageBoxImage.Information)
            {
                if (isSuccess)
                    HandyControl.Controls.Growl.Success(CreateGrowlInfo(messageBoxText, InfoType.Success));
                else
                    HandyControl.Controls.Growl.Info(CreateGrowlInfo(messageBoxText, InfoType.Info));
                return MessageBoxResult.OK;
            }

            // 默认根据内容推断
            if (isSuccess)
                HandyControl.Controls.Growl.Success(CreateGrowlInfo(messageBoxText, InfoType.Success));
            else if (caption.Contains("错误") || messageBoxText.Contains("失败") || caption.Contains("Error") || messageBoxText.Contains("Fail"))
                HandyControl.Controls.Growl.Error(CreateGrowlInfo(messageBoxText, InfoType.Error));
            else if (caption.Contains("警告") || caption.Contains("Warning"))
                HandyControl.Controls.Growl.Warning(CreateGrowlInfo(messageBoxText, InfoType.Warning));
            else
                HandyControl.Controls.Growl.Info(CreateGrowlInfo(messageBoxText, InfoType.Info));

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

    private static GrowlInfo CreateGrowlInfo(string message, InfoType type)
    {
        return new GrowlInfo
        {
            Message = message,
            Type = type,
            Token = ToastToken,
            ShowDateTime = true,
            ShowCloseButton = true,
            StaysOpen = false,
            WaitTime = ToastWaitTimeSeconds
        };
    }
}
