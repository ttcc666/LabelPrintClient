using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Template.Views;

public partial class TemplateEditWindow : Window
{
    public new LabelTemplate Template { get; private set; }
    public bool ResetSerialCounter { get; private set; } = false;

    public TemplateEditWindow(LabelTemplate? template = null)
    {
        InitializeComponent();

        if (template != null)
        {
            // 编辑模式，深拷贝
            Template = new LabelTemplate
            {
                Id = template.Id,
                CategoryId = template.CategoryId,
                Name = template.Name,
                StorageType = template.StorageType,
                TemplatePath = template.TemplatePath,
                TemplateFileName = template.TemplateFileName,
                TemplateContent = template.TemplateContent,
                TemplateHash = template.TemplateHash,
                DataSourceName = template.DataSourceName,
                Version = template.Version,
                IsEnabled = template.IsEnabled,
                IsSerialNumber = template.IsSerialNumber,
                SerialNumberPrefix = template.SerialNumberPrefix,
                SerialNumberPattern = template.SerialNumberPattern,
                SerialResetPeriod = template.SerialResetPeriod,
                CurrentSerialValue = template.CurrentSerialValue,
                CreateTime = template.CreateTime,
                UpdateTime = template.UpdateTime
            };
            TitleText.Text = "编辑模板";
            NameBox.Text = Template.Name;
            IsEnabledBox.IsChecked = Template.IsEnabled;
            IsSerialNumberBox.IsChecked = Template.IsSerialNumber;
            SerialNumberPatternBox.Text = SerialNumberService.NormalizePattern(Template.SerialNumberPattern, Template.SerialNumberPrefix);
            SelectSerialResetPeriod(Template.SerialResetPeriod);

            // 选中存储介质
            if (Template.StorageType == TemplateStorageType.Database)
            {
                StorageTypeBox.SelectedIndex = 1;
            }
            else
            {
                StorageTypeBox.SelectedIndex = 0;
            }
        }
        else
        {
            Template = new LabelTemplate
            {
                IsEnabled = true
            };
            TitleText.Text = "新增模板";

            // 根据 App.Settings.RunMode 自动决定默认存储介质
            if (App.Settings.RunMode == AppRunMode.LocalSqlite)
            {
                StorageTypeBox.SelectedIndex = 0; // LocalFile
            }
            else
            {
                StorageTypeBox.SelectedIndex = 1; // Database
            }
        }

        // 根据当前的数据库模式，强制限制下拉框的可选状态
        ApplyStorageTypeLimits();
        UpdateSerialPreview();
    }

    private void ApplyStorageTypeLimits()
    {
        if (App.Settings.RunMode == AppRunMode.LocalSqlite)
        {
            // SQLite 模式：只能存本地物理文件，禁用数据库存储
            foreach (var item in StorageTypeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), "Database", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsEnabled = false;
                }
            }
        }
        else if (App.Settings.RunMode == AppRunMode.LanPostgreSql)
        {
            // PgSQL 模式：只能存数据库，禁用本地物理文件
            foreach (var item in StorageTypeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), "LocalFile", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsEnabled = false;
                }
            }
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppMessageBox.Show("请输入模板名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isSerial = IsSerialNumberBox.IsChecked == true;
        var pattern = SerialNumberPatternBox.Text.Trim();
        if (isSerial && !SerialNumberService.IsValidPattern(pattern, out var patternError))
        {
            AppMessageBox.Show(patternError, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 检测是否修改了序列号生成规则
        var oldIsSerial = Template.IsSerialNumber == true;
        var oldPattern = Template.SerialNumberPattern ?? string.Empty;
        var oldPeriod = Template.SerialResetPeriod;
        var newPeriod = isSerial ? ReadSerialResetPeriod() : SerialResetPeriod.Never;

        bool isRuleChanged = false;
        if (oldIsSerial)
        {
            // 以前就是序列号模式，现在关闭了，或者格式变了，或者重置周期变了
            if (!isSerial || oldPattern != pattern || oldPeriod != newPeriod)
            {
                isRuleChanged = true;
            }
        }
        else if (isSerial)
        {
            // 以前不是，现在开启了
            isRuleChanged = true;
        }

        // 只有在原先存在序列号计数历史且本次确实修改了规则时，提示是否重置流水号
        if (oldIsSerial && isRuleChanged)
        {
            var confirmResult = AppMessageBox.Show(
                "检测到您修改了序列号生成规则，是否需要将当前流水号计数器重置为初始状态 (从1开始)？\n\n点击【是】将计数重置为 0；\n点击【否】将继续保留并累加当前已有的流水计数。",
                "重置流水号确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult == MessageBoxResult.Yes)
            {
                ResetSerialCounter = true;
            }
        }

        Template.Name = name;
        Template.IsEnabled = IsEnabledBox.IsChecked == true;
        Template.IsSerialNumber = isSerial;
        Template.SerialNumberPattern = isSerial ? pattern : null;
        Template.SerialNumberPrefix = null;
        Template.SerialResetPeriod = isSerial ? ReadSerialResetPeriod() : SerialResetPeriod.Never;

        // 读取存储介质
        if (StorageTypeBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            Template.StorageType = tag == "Database"
                ? TemplateStorageType.Database
                : TemplateStorageType.LocalFile;
        }

        DialogResult = true;
        Close();
    }

    private void SerialRule_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSerialPreview();
    }

    private void UpdateSerialPreview()
    {
        if (SerialPreviewText == null || SerialNumberPatternBox == null)
            return;

        var pattern = SerialNumberPatternBox.Text.Trim();
        SerialPreviewText.Text = SerialNumberService.IsValidPattern(pattern, out var error)
            ? $"预览：{SerialNumberService.Preview(pattern, DateTime.Now)}"
            : $"预览：{error}";
    }

    private SerialResetPeriod ReadSerialResetPeriod()
    {
        var tag = (SerialResetPeriodBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<SerialResetPeriod>(tag, out var period) ? period : SerialResetPeriod.Never;
    }

    private void SelectSerialResetPeriod(SerialResetPeriod period)
    {
        foreach (var item in SerialResetPeriodBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), period.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                SerialResetPeriodBox.SelectedItem = item;
                return;
            }
        }
        SerialResetPeriodBox.SelectedIndex = 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
