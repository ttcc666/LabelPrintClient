using System.Windows;
using LabelPrintClient.Modules.Auth.Models;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Auth.Views;

public partial class UserEditWindow : HandyControl.Controls.Window
{
    private readonly bool _isNew;

    public UserEditWindow(AuthUser? user = null)
    {
        InitializeComponent();
        _isNew = user == null;
        User = user == null
            ? new AuthUser()
            : new AuthUser
            {
                Id = user.Id,
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                PasswordHash = user.PasswordHash,
                PasswordSalt = user.PasswordSalt,
                PasswordIterations = user.PasswordIterations,
                IsEnabled = user.IsEnabled,
                CreateTime = user.CreateTime,
                LastLoginTime = user.LastLoginTime
            };

        TitleText.Text = _isNew ? "新增用户" : "编辑用户";
        UserNameBox.Text = User.UserName;
        UserNameBox.IsEnabled = _isNew;
        DisplayNameBox.Text = User.DisplayName;
        IsEnabledBox.IsChecked = User.IsEnabled;
        PasswordLabel.Visibility = _isNew ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Visibility = _isNew ? Visibility.Visible : Visibility.Collapsed;
    }

    public AuthUser User { get; }

    public string Password => PasswordBox.Password;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var userName = UserNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            AppMessageBox.Show("用户名不能为空。");
            return;
        }

        if (_isNew && PasswordBox.Password.Length < 6)
        {
            AppMessageBox.Show("密码至少 6 位。");
            return;
        }

        User.UserName = userName;
        User.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text)
            ? userName
            : DisplayNameBox.Text.Trim();
        User.IsEnabled = IsEnabledBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
