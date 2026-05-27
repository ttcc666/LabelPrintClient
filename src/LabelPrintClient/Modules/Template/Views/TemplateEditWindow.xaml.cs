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
    public bool TemplateModeChanged { get; private set; }

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

            TitleText.Text = AppLanguageService.GetString("Template.EditActionTitle");
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

            TitleText.Text = AppLanguageService.GetString("Template.AddTemplate");
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
            AppMessageBox.Show(AppLanguageService.GetString("Template.NameRequired"), AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = ReadTemplateMode();
        var pattern = RulePatternBox.Text.Trim();
        if (mode == LabelTemplateMode.Batch &&
            !SerialNumberService.IsValidBatchPattern(pattern, _allowedFields, out var batchPatternError))
        {
            AppMessageBox.Show(batchPatternError, AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (mode == LabelTemplateMode.Serialized &&
            !SerialNumberService.IsValidPattern(pattern, _allowedFields, out var serialPatternError))
        {
            AppMessageBox.Show(serialPatternError, AppLanguageService.GetString("Common.Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newPeriod = mode == LabelTemplateMode.Serialized ? ReadSerialResetPeriod() : Template.SerialResetPeriod;
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
                AppLanguageService.GetString("Template.ResetSerialConfirm"),
                AppLanguageService.GetString("Template.ResetSerialConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            ResetSerialCounter = confirmResult == MessageBoxResult.Yes;
        }

        if (batchRuleChanged)
        {
            var confirmResult = AppMessageBox.Show(
                AppLanguageService.GetString("Template.ClearBatchConfirm"),
                AppLanguageService.GetString("Template.ClearBatchConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            ClearLockedBatchNumbers = confirmResult == MessageBoxResult.Yes;
        }

        Template.Name = name;
        Template.IsEnabled = IsEnabledBox.IsChecked == true;
        Template.TemplateMode = mode;
        TemplateModeChanged = _originalMode != mode;
        Template.IsSerialNumber = mode == LabelTemplateMode.Serialized;
        if (mode == LabelTemplateMode.Batch)
            Template.BatchNumberPattern = normalizedBatchPattern;
        else if (string.IsNullOrWhiteSpace(Template.BatchNumberPattern))
            Template.BatchNumberPattern = SerialNumberService.DefaultBatchPattern;

        if (mode == LabelTemplateMode.Serialized)
            Template.SerialNumberPattern = SerialNumberService.NormalizePattern(pattern, null);
        else if (string.IsNullOrWhiteSpace(Template.SerialNumberPattern))
            Template.SerialNumberPattern = SerialNumberService.DefaultPattern;
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
            RuleExampleText.Text = AppLanguageService.GetString("Template.RuleExampleBatch");
            RuleVariableText.Text = AppLanguageService.GetString("Template.RuleVariablesBatch");
            RulePreviewText.Text = SerialNumberService.IsValidBatchPattern(RulePatternBox.Text.Trim(), _allowedFields, out var error)
                ? AppLanguageService.Format("Template.Preview", SerialNumberService.PreviewBatch(RulePatternBox.Text.Trim(), DateTime.Now))
                : AppLanguageService.Format("Template.Preview", error);
            return;
        }

        if (mode == LabelTemplateMode.Serialized)
        {
            RuleExampleText.Text = AppLanguageService.GetString("Template.RuleExampleSerial");
            RuleVariableText.Text = AppLanguageService.GetString("Template.RuleVariablesSerial");
            RulePreviewText.Text = SerialNumberService.IsValidPattern(RulePatternBox.Text.Trim(), _allowedFields, out var error)
                ? AppLanguageService.Format("Template.Preview", SerialNumberService.Preview(RulePatternBox.Text.Trim(), DateTime.Now))
                : AppLanguageService.Format("Template.Preview", error);
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
