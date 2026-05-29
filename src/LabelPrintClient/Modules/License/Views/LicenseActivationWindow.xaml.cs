using System;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;
using LabelPrintClient.Modules.License.Services;

namespace LabelPrintClient.Modules.License.Views;

public partial class LicenseActivationWindow : Window
{
    private readonly AppSettings _settings;

    public LicenseActivationWindow(AppSettings settings, LicenseResult? initialResult = null)
    {
        _settings = settings;
        InitializeComponent();
        MachineCodeBox.Text = LicenseManager.MachineCode;
        ProductCodeBox.Text = settings.ProductCode;
        LicenseFilePathBox.Text = settings.StandaloneLicenseFilePath;
        LicenseServerUrlBox.Text = settings.LicenseServerUrl;
        LicenseAccessKeyBox.Password = settings.LicenseAccessKey;
        SelectMode(settings.LicenseMode);
        UpdateModeVisibility(immediate: true);
        ShowStatus(initialResult?.Message, immediate: true);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            this.DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void BrowseLicenseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "License files|*.json;*.license|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
            LicenseFilePathBox.Text = dialog.FileName;
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out var errorMessage))
        {
            ShowStatus(errorMessage);
            return;
        }

        AppConfigService.Save(_settings);
        LicenseManager.Initialize(_settings);
        var result = await LicenseManager.ValidateStartupAsync();
        if (result.IsValid)
        {
            DialogResult = true;
            return;
        }

        ShowStatus(result.Message);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private bool TryBuildSettings(out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!TryGetSelectedMode(out var mode))
        {
            errorMessage = "请选择授权模式。";
            return false;
        }

        var productCode = ProductCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            errorMessage = "产品编码不能为空。";
            return false;
        }

        _settings.LicenseMode = mode;
        _settings.ProductCode = productCode;
        _settings.StandaloneLicenseFilePath = LicenseFilePathBox.Text.Trim();
        _settings.LicenseServerUrl = LicenseServerUrlBox.Text.Trim();
        _settings.LicenseAccessKey = LicenseAccessKeyBox.Password.Trim();
        return true;
    }

    private void SelectMode(LicenseMode mode)
    {
        foreach (var item in LicenseModeBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                LicenseModeBox.SelectedItem = item;
                return;
            }
        }

        LicenseModeBox.SelectedIndex = 0;
    }

    private bool TryGetSelectedMode(out LicenseMode mode)
    {
        mode = LicenseMode.Standalone;
        return LicenseModeBox.SelectedItem is ComboBoxItem item &&
               Enum.TryParse(item.Tag?.ToString(), out mode);
    }

    private void LicenseModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateModeVisibility();
    }

    private void UpdateModeVisibility(bool immediate = false)
    {
        if (LicenseModeBox == null) return;

        if (TryGetSelectedMode(out var mode))
        {
            // 切换授权模式时，自动清空旧模式产生的过期报错卡片，彻底避免视觉混淆
            ShowStatus(null, immediate);

            if (mode == LicenseMode.Standalone)
            {
                AnimatePanel(StandalonePanel, 60, 1.0, true, immediate);
                AnimatePanel(FloatingServerPanel, 0, 0.0, false, immediate);
                AnimatePanel(FloatingKeyPanel, 0, 0.0, false, immediate);
            }
            else
            {
                AnimatePanel(StandalonePanel, 0, 0.0, false, immediate);
                AnimatePanel(FloatingServerPanel, 60, 1.0, true, immediate);
                AnimatePanel(FloatingKeyPanel, 60, 1.0, true, immediate);
            }
        }
    }

    private void ShowStatus(string? message, bool immediate = false)
    {
        ShowStatus(message, isSuccess: false, immediate);
    }

    private void ShowStatus(string? message, bool isSuccess, bool immediate = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = string.Empty;
            AnimatePanel(StatusBorder, 0, 0.0, false, immediate);
        }
        else
        {
            // 物理释放并清除上一次动画残留的 HeightProperty 锁定，彻底杜绝高度累加与发散 Bug！
            StatusBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            StatusBorder.Height = double.NaN;

            StatusText.Text = message;

            // 根据状态类型，实现水晶级水透粉/水透绿双态优雅配色渲染
            if (isSuccess)
            {
                StatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#08F0FDF4")); // 超轻水透薄荷绿
                StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#18DCFCE7")); // 细密淡绿描边
                var greenBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#16A34A"));
                StatusText.Foreground = greenBrush;
                StatusIcon.Foreground = greenBrush;
                StatusIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.CheckCircle;
            }
            else
            {
                StatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#08FEE2E2")); // 超轻水透水蜜桃红
                StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#18FECACA")); // 细密淡红描边
                var redBrush = (System.Windows.Media.Brush)FindResource("AppDangerBrush");
                StatusText.Foreground = redBrush;
                StatusIcon.Foreground = redBrush;
                StatusIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.AlertCircle;
            }

            // 高阶动态高度测算，杜绝超长文字被硬编码高度裁剪
            StatusBorder.Measure(new System.Windows.Size(510, double.PositiveInfinity));
            double targetHeight = StatusBorder.DesiredSize.Height;
            if (targetHeight < 48) targetHeight = 48; // 保底高度限制

            AnimatePanel(StatusBorder, targetHeight, 1.0, true, immediate);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var serverUrl = LicenseServerUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            ShowStatus("请输入浮动授权服务器地址。");
            return;
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out _))
        {
            ShowStatus("请输入有效的服务器 URL（例如 http://127.0.0.1:5188）。");
            return;
        }

        // 禁用测试按钮以防重复点击
        var btn = sender as System.Windows.Controls.Button;
        if (btn != null) btn.IsEnabled = false;
        ShowStatus("正在测试连通性，请稍候...", isSuccess: false, immediate: false);

        try
        {
            // 每次测试均采用临时 Client 和 HttpClient，彻底避免 HttpClient BaseAddress 物理锁死异常
            using var http = new HttpClient();
            var tempClient = new FloatingLicenseClient(http);
            var tempSettings = new AppSettings
            {
                LicenseMode = LicenseMode.Floating,
                LicenseServerUrl = serverUrl,
                ProductCode = ProductCodeBox.Text.Trim(),
                LicenseAccessKey = LicenseAccessKeyBox.Password.Trim()
            };

            var result = await tempClient.AcquireAsync(tempSettings);

            if (result.Status == LicenseStatus.ServerUnavailable)
            {
                ShowStatus($"连接失败：授权服务器不可用，请检查地址是否正确或服务是否启动。", isSuccess: false);
            }
            else if (result.Status == LicenseStatus.InvalidConfiguration)
            {
                ShowStatus($"配置错误：{result.Message}", isSuccess: false);
            }
            else
            {
                // 网络接通，界面反馈高雅冷白绿
                ShowStatus($"【连通测试成功】已成功连通授权服务器！当前授权状态：{result.Message}", isSuccess: true);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"连接失败：{ex.Message}", isSuccess: false);
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    /// <summary>
    /// 提供极致丝滑的三维弹性阻尼高度与透明度联合动画
    /// </summary>
    private void AnimatePanel(FrameworkElement panel, double targetHeight, double targetOpacity, bool show, bool immediate)
    {
        if (panel == null) return;

        if (immediate)
        {
            panel.Height = targetHeight;
            panel.Opacity = targetOpacity;
            panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (show)
        {
            panel.Visibility = Visibility.Visible;
        }

        // 物理计算绝对非 NaN、安全的动画高度起始实数值 (防止 Height 属性为 NaN 导致插值崩溃)
        double fromHeight = double.IsNaN(panel.Height) || panel.Visibility != Visibility.Visible ? 0 : panel.ActualHeight;
        if (!show)
        {
            fromHeight = panel.ActualHeight;
        }

        // 物理计算绝对安全的动画透明度起始实数值
        double fromOpacity = panel.Visibility != Visibility.Visible ? 0 : panel.Opacity;
        if (!show)
        {
            fromOpacity = 1.0;
        }

        var duration = TimeSpan.FromSeconds(0.32);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 显式指定 From 起始值，连根拔起 NaN 插值异常
        var heightAnim = new DoubleAnimation
        {
            From = fromHeight,
            To = targetHeight,
            Duration = duration,
            EasingFunction = ease
        };

        // 启动透明度淡入淡出动画
        var opacityAnim = new DoubleAnimation
        {
            From = fromOpacity,
            To = targetOpacity,
            Duration = duration,
            EasingFunction = ease
        };

        if (!show)
        {
            opacityAnim.Completed += (s, e) =>
            {
                // 动画完全结束且面板确实透明时，收回布局占位
                if (panel.Opacity == 0)
                {
                    panel.Visibility = Visibility.Collapsed;
                }
            };
        }

        panel.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
        panel.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
    }
}
