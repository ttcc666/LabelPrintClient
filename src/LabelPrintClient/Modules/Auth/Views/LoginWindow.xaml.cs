using System.Windows;
using System.Windows.Input;
using LabelPrintClient.Modules.Auth.Services;

namespace LabelPrintClient.Modules.Auth.Views;

public partial class LoginWindow : HandyControl.Controls.Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UserNameBox.Focus();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        await LoginAsync();
    }

    private async void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await LoginAsync();
    }

    private async Task LoginAsync()
    {
        ErrorText.Text = string.Empty;
        try
        {
            await AuthService.LoginAsync(UserNameBox.Text, PasswordBox.Password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
