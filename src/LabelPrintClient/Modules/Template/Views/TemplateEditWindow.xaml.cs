using System.Windows;
using System.Windows.Controls;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Template.Views;

public partial class TemplateEditWindow : Window
{
    private readonly IEnumerable<string>? _allowedFields;
    private readonly LabelTemplateMode _originalMode;
    private readonly string _originalSerialPattern;
    private readonly string _originalBatchPattern;
    private readonly SerialResetPeriod _originalSerialResetPeriod;

    public new LabelTemplate Template { get; private set; }
    public bool ResetSerialCounter { get; private set; }
    public bool ClearLockedBatchNumbers { get; private set; }

    public TemplateEditWindow(LabelTemplate? template = null, IEnumerable<string>? allowedFields = null)
    {
        _allowedFields = allowedFields;
        InitializeComponent();

        if (template != null)
        {
            Template = CopyTemplate(template);
            _originalMode = Template.TemplateMode;
            _originalSerialPattern = Template.SerialNumberPattern ?? string.Empty;
            _originalBatchPattern = Template.BatchNumberPattern ?? string.Empty;
            _originalSerialResetPeriod = Template.SerialResetPeriod;

            TitleText.Text = "编辑模板";
            NameBox.Text = Template.Name;
            IsEnabledBox.IsChecked = Template.IsEnabled;
            SelectTemplateMode(Template.TemplateMode);
            SelectSerialResetPeriod(Template.SerialResetPeriod);
            ApplyPatternForMode(Template.TemplateMode);
            StorageTypeBox.SelectedIndex = Template.StorageType == TemplateStorageType.Database ? 1 : 0;
        }
        else
        {
            Template = new LabelTemplate
            {
                IsEnabled = true,
                TemplateMode = LabelTemplateMode.Normal,
                IsSerialNumber = false,
                BatchNumberPattern = SerialNumberService.DefaultBatchPattern,
                SerialNumberPattern = SerialNumberService.DefaultPattern,
                SerialResetPeriod = SerialResetPeriod.Never
            };
            _originalMode = Template.TemplateMode;
            _originalSerialPattern = Template.SerialNumberPattern ?? string.Empty;
            _originalBatchPattern = Template.BatchNumberPattern ?? string.Empty;
            _originalSerialResetPeriod = Template.SerialResetPeriod;

            TitleText.Text = "新增模板";
            SelectTemplateMode(LabelTemplateMode.Normal);
            StorageTypeBox.SelectedIndex = App.Settings.RunMode == AppRunMode.LocalSqlite ? 0 : 1;
        }

        ApplyStorageTypeLimits();
        UpdateRulePanel();
    }

    private static LabelTemplate CopyTemplate(LabelTemplate template)
    {
        return new LabelTemplate
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
            TemplateMode = template.TemplateMode,
            IsSerialNumber = template.IsSerialNumber,
            SerialNumberPrefix = template.SerialNumberPrefix,
            SerialNumberPattern = template.SerialNumberPattern,
            BatchNumberPattern = template.BatchNumberPattern,
            SerialResetPeriod = template.SerialResetPeriod,
            CurrentSerialValue = template.CurrentSerialValue,
            CreateTime = template.CreateTime,
            UpdateTime = template.UpdateTime
        };
    }

    private void ApplyStorageTypeLimits()
    {
        if (App.Settings.RunMode == AppRunMode.LocalSqlite)
        {
            foreach (var item in StorageTypeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), "Database", StringComparison.OrdinalIgnoreCase))
                    item.IsEnabled = false;
            }
        }
        else if (App.Settings.RunMode == AppRunMode.LanPostgreSql)
        {
            foreach (var item in StorageTypeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), "LocalFile", StringComparison.OrdinalIgnoreCase))
                    item.IsEnabled = false;
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

        var mode = ReadTemplateMode();
        var pattern = RulePatternBox.Text.Trim();
        if (mode == LabelTemplateMode.Batch &&
            !SerialNumberService.IsValidBatchPattern(pattern, _allowedFields, out var batchPatternError))
        {
            AppMessageBox.Show(batchPatternError, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (mode == LabelTemplateMode.Serialized &&
            !SerialNumberService.IsValidPattern(pattern, _allowedFields, out var serialPatternError))
        {
            AppMessageBox.Show(serialPatternError, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newPeriod = mode == LabelTemplateMode.Serialized ? ReadSerialResetPeriod() : SerialResetPeriod.Never;
        var serialRuleChanged = _originalMode == LabelTemplateMode.Serialized &&
                                (mode != LabelTemplateMode.Serialized ||
                                 !string.Equals(_originalSerialPattern, pattern, StringComparison.Ordinal) ||
                                 _originalSerialResetPeriod != newPeriod);
        var normalizedBatchPattern = mode == LabelTemplateMode.Batch
            ? SerialNumberService.NormalizeBatchPattern(pattern)
            : string.Empty;
        var batchRuleChanged = _originalMode == LabelTemplateMode.Batch &&
                               (mode != LabelTemplateMode.Batch ||
                                !string.Equals(
                                    SerialNumberService.NormalizeBatchPattern(_originalBatchPattern),
                                    normalizedBatchPattern,
                                    StringComparison.Ordinal));

        if (serialRuleChanged)
        {
            var confirmResult = AppMessageBox.Show(
                "检测到您修改了序列号生成规则，是否需要将当前流水号计数器重置为初始状态 (从1开始)？\n\n点击【是】将计数重置为 0；\n点击【否】将继续保留并累加当前已有的流水计数。",
                "重置流水号确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            ResetSerialCounter = confirmResult == MessageBoxResult.Yes;
        }

        if (batchRuleChanged)
        {
            var confirmResult = AppMessageBox.Show(
                "检测到您修改了批次号生成规则，是否需要清空当前导入数据中已打印绑定的批次号？\n\n点击【是】将清空导入行已锁定的批次号，下次再次打印时会按新规则重新生成；\n点击【否】将保留当前已锁定的批次号。",
                "清空批次号确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            ClearLockedBatchNumbers = confirmResult == MessageBoxResult.Yes;
        }

        Template.Name = name;
        Template.IsEnabled = IsEnabledBox.IsChecked == true;
        Template.TemplateMode = mode;
        Template.IsSerialNumber = mode == LabelTemplateMode.Serialized;
        Template.BatchNumberPattern = mode == LabelTemplateMode.Batch
            ? normalizedBatchPattern
            : null;
        Template.SerialNumberPattern = mode == LabelTemplateMode.Serialized
            ? SerialNumberService.NormalizePattern(pattern, null)
            : null;
        Template.SerialNumberPrefix = null;
        Template.SerialResetPeriod = newPeriod;

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

    private void Rule_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;

        if (sender == TemplateModeBox)
            ApplyPatternForMode(ReadTemplateMode());

        UpdateRulePanel();
    }

    private void ApplyPatternForMode(LabelTemplateMode mode)
    {
        if (RulePatternBox == null)
            return;

        RulePatternBox.Text = mode switch
        {
            LabelTemplateMode.Batch => SerialNumberService.NormalizeBatchPattern(Template.BatchNumberPattern),
            LabelTemplateMode.Serialized => SerialNumberService.NormalizePattern(Template.SerialNumberPattern, Template.SerialNumberPrefix),
            _ => string.Empty
        };
    }

    private void UpdateRulePanel()
    {
        if (RulePanel == null || RulePatternBox == null || RulePreviewText == null)
            return;

        var mode = ReadTemplateMode();
        var hasRule = mode != LabelTemplateMode.Normal;
        RulePanel.IsEnabled = hasRule;
        RulePanel.Visibility = hasRule ? Visibility.Visible : Visibility.Collapsed;
        ResetPeriodLabel.Visibility = mode == LabelTemplateMode.Serialized ? Visibility.Visible : Visibility.Collapsed;
        SerialResetPeriodBox.Visibility = mode == LabelTemplateMode.Serialized ? Visibility.Visible : Visibility.Collapsed;

        if (mode == LabelTemplateMode.Batch)
        {
            RuleExampleText.Text = "示例：BATCH-{yyyy}{MM}{dd}";
            RuleVariableText.Text = "变量：{yyyy} {yy} {MM} {dd} {HH} {mm}，可引用字段编码";
            RulePreviewText.Text = SerialNumberService.IsValidBatchPattern(RulePatternBox.Text.Trim(), _allowedFields, out var error)
                ? $"预览：{SerialNumberService.PreviewBatch(RulePatternBox.Text.Trim(), DateTime.Now)}"
                : $"预览：{error}";
            return;
        }

        if (mode == LabelTemplateMode.Serialized)
        {
            RuleExampleText.Text = "示例：SN-{yyyy}{MM}{dd}-{seq:0000}";
            RuleVariableText.Text = "变量：{yyyy} {yy} {MM} {dd} {HH} {mm} {seq:0000}，可引用字段编码";
            RulePreviewText.Text = SerialNumberService.IsValidPattern(RulePatternBox.Text.Trim(), _allowedFields, out var error)
                ? $"预览：{SerialNumberService.Preview(RulePatternBox.Text.Trim(), DateTime.Now)}"
                : $"预览：{error}";
        }
    }

    private LabelTemplateMode ReadTemplateMode()
    {
        var tag = (TemplateModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<LabelTemplateMode>(tag, out var mode) ? mode : LabelTemplateMode.Normal;
    }

    private void SelectTemplateMode(LabelTemplateMode mode)
    {
        foreach (var item in TemplateModeBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                TemplateModeBox.SelectedItem = item;
                return;
            }
        }
        TemplateModeBox.SelectedIndex = 0;
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
