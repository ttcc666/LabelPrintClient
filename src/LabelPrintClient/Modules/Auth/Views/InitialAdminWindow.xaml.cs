using System.Windows;
using LabelPrintClient.Modules.Auth.Services;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Auth.Views;

public partial class InitialAdminWindow : HandyControl.Controls.Window
{
    public InitialAdminWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            HandyControl.Controls.Growl.Register(AppMessageBox.ToastToken, ToastHost);
            UserNameBox.Focus();
        };
        Closed += (_, _) => HandyControl.Controls.Growl.Unregister(AppMessageBox.ToastToken, ToastHost);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var userName = UserNameBox.Text.Trim();
        var displayName = DisplayNameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName))
        {
            AppMessageBox.Warning("用户名不能为空。");
            return;
        }

        if (password.Length < 6)
        {
            AppMessageBox.Warning("密码至少 6 位。");
            return;
        }

        if (!string.Equals(password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            AppMessageBox.Warning("两次输入的密码不一致。");
            return;
        }

        try
        {
            await PermissionBootstrapper.CreateInitialAdministratorAsync(userName, displayName, password);
            await AuthService.LoginAsync(userName, password);
            AppMessageBox.Success("系统管理员初始化成功！");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppMessageBox.Error(ex.Message, "创建管理员失败");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
