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
            AppMessageBox.Warning(AppLanguageService.GetString("Account.UserNameRequired"));
            return;
        }

        if (password.Length < 6)
        {
            AppMessageBox.Warning(AppLanguageService.GetString("Account.PasswordTooShort"));
            return;
        }

        if (!string.Equals(password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            AppMessageBox.Warning(AppLanguageService.GetString("Account.PasswordMismatch"));
            return;
        }

        try
        {
            await PermissionBootstrapper.CreateInitialAdministratorAsync(userName, displayName, password);
            await AuthService.LoginAsync(userName, password);
            AppMessageBox.Success(AppLanguageService.GetString("Account.InitialAdminSuccess"));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppMessageBox.Error(ex.Message, AppLanguageService.GetString("Account.CreateAdminFailed"));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
