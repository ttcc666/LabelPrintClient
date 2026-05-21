using System.Data;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Modules.Template.Services;
using Stimulsoft.Report;
using Stimulsoft.Report.Components;

namespace LabelPrintClient.Modules.Template.Services;

public class StiTemplateDesignerService
{
    private readonly ILabelTemplateStorageService _storage;
    private static bool _isDesignerEventRegistered = false;
    private static readonly object _lock = new object();

    public StiTemplateDesignerService(ILabelTemplateStorageService storage)
    {
        _storage = storage;
        EnsureDesignerEventRegistered();
    }

    private static void EnsureDesignerEventRegistered()
    {
        if (_isDesignerEventRegistered) return;
        lock (_lock)
        {
            if (_isDesignerEventRegistered) return;

            Stimulsoft.Report.Design.StiDesigner.SavingReport += (sender, e) =>
            {
                try
                {
                    var designerControl = sender as Stimulsoft.Report.Design.StiDesignerControl;
                    var report = designerControl?.Report;
                    if (report?.Tag is DesignerSaveContext context)
                    {
                        // 同步执行底层的双介质保存
                        context.Storage.SaveReportAsync(context.Template, report, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                        
                        // 声明已成功自定义处理保存，阻止弹出“另存为”对话框
                        e.Processed = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"保存模板失败：{ex.Message}", 
                        "错误", 
                        System.Windows.MessageBoxButton.OK, 
                        System.Windows.MessageBoxImage.Error);
                }
            };

            _isDesignerEventRegistered = true;
        }
    }

    public async Task DesignAsync(
        LabelTemplate template,
        IReadOnlyList<LabelTemplateField> fields,
        CancellationToken cancellationToken = default)
    {
        var report = await StaThreadRunner.RunAsync(() =>
        {
            var designReport = LoadOrCreateReport(template);
            RegisterDesignData(designReport, template.DataSourceName, fields);

            // 将当前模板和存储服务封装并放入 Tag 中，供静态全局保存事件提取
            designReport.Tag = new DesignerSaveContext(template, _storage);

            designReport.Design(true);
            return designReport;
        }, cancellationToken).ConfigureAwait(false);

        await _storage.SaveReportAsync(template, report, cancellationToken).ConfigureAwait(false);
    }

    private StiReport LoadOrCreateReport(LabelTemplate template)
    {
        try
        {
            return _storage.LoadReport(template);
        }
        catch
        {
            var report = new StiReport();
            report.Pages.Clear();
            var page = new StiPage();
            page.Name = "Page1";
            report.Pages.Add(page);
            return report;
        }
    }

    private static void RegisterDesignData(StiReport report, string dataSourceName, IReadOnlyList<LabelTemplateField> fields)
    {
        var table = new DataTable(dataSourceName);
        foreach (var field in fields)
        {
            if (!table.Columns.Contains(field.FieldCode))
                table.Columns.Add(field.FieldCode, typeof(string));
        }

        var row = table.NewRow();
        foreach (var field in fields)
        {
            row[field.FieldCode] = $"示例{field.FieldName}";
        }
        table.Rows.Add(row);

        report.Dictionary.Databases.Clear();
        report.RegData(dataSourceName, table);
        report.Dictionary.Synchronize();

        // 绑定中文列别名 (Alias)，使设计器右侧的数据字典树清晰展示中文含义
        var dataSource = report.Dictionary.DataSources[dataSourceName];
        if (dataSource != null)
        {
            dataSource.Alias = "标签数据源";

            foreach (var field in fields)
            {
                var column = dataSource.Columns[field.FieldCode];
                if (column != null)
                {
                    column.Alias = field.FieldName; // 将别名设置为维护的中文名称，例如 “商品名称”
                }
            }
        }
    }

    // 辅助类用于携带保存上下文
    private class DesignerSaveContext
    {
        public LabelTemplate Template { get; }
        public ILabelTemplateStorageService Storage { get; }

        public DesignerSaveContext(LabelTemplate template, ILabelTemplateStorageService storage)
        {
            Template = template;
            Storage = storage;
        }
    }
}
