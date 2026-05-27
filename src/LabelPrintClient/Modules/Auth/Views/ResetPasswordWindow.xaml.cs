using System.Windows;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Auth.Views;

public partial class ResetPasswordWindow : HandyControl.Controls.Window
{
    public ResetPasswordWindow()
    {
        InitializeComponent();
    }

    public string Password => PasswordBox.Password;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Password.Length < 6)
        {
            AppMessageBox.Show("密码至少 6 位。");
            return;
        }

        if (!string.Equals(PasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            AppMessageBox.Show("两次输入的密码不一致。");
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
