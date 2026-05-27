using System.Windows;
using LabelPrintClient.Modules.Auth.Models;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Auth.Views;

public partial class RoleEditWindow : HandyControl.Controls.Window
{
    private readonly bool _isNew;

    public RoleEditWindow(AuthRole? role = null)
    {
        InitializeComponent();
        _isNew = role == null;
        Role = role == null
            ? new AuthRole()
            : new AuthRole
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Name,
                IsSystem = role.IsSystem,
                IsEnabled = role.IsEnabled,
                Sort = role.Sort,
                CreateTime = role.CreateTime
            };
        TitleText.Text = _isNew ? "新增角色" : "编辑角色";
        CodeBox.Text = Role.Code;
        CodeBox.IsEnabled = _isNew && !Role.IsSystem;
        NameBox.Text = Role.Name;
        IsEnabledBox.IsChecked = Role.IsEnabled;
    }

    public AuthRole Role { get; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            AppMessageBox.Show("角色编码和名称不能为空。");
            return;
        }

        Role.Code = code;
        Role.Name = name;
        Role.IsEnabled = IsEnabledBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
