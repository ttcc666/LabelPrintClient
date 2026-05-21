using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using Stimulsoft.Report;
using System.IO;

namespace LabelPrintClient.Services.TemplateStorage;

public class LocalFileTemplateStorageService : ILabelTemplateStorageService
{
    public StiReport LoadReport(LabelTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.TemplatePath))
            throw new InvalidOperationException("模板文件路径为空，请先上传或设计模板。");

        if (!File.Exists(template.TemplatePath))
            throw new FileNotFoundException("模板文件不存在。", template.TemplatePath);

        var report = new StiReport();
        report.Load(template.TemplatePath);
        return report;
    }

    public Task<StiReport> LoadReportAsync(LabelTemplate template, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => LoadReport(template), cancellationToken);
    }

    public async Task SaveReportAsync(LabelTemplate template, StiReport report, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(template.TemplatePath))
            throw new InvalidOperationException("模板文件路径为空，无法保存。");

        await Task.Run(() =>
        {
            var dir = Path.GetDirectoryName(template.TemplatePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            report.Save(template.TemplatePath);
            template.TemplateFileName = string.IsNullOrWhiteSpace(template.TemplateFileName)
                ? Path.GetFileName(template.TemplatePath)
                : template.TemplateFileName;
        }, cancellationToken).ConfigureAwait(false);

        template.TemplateHash = File.Exists(template.TemplatePath)
            ? await FileHashHelper.GetSha256Async(template.TemplatePath, cancellationToken).ConfigureAwait(false)
            : null;
        template.UpdateTime = DateTime.Now;

        await AppDb.Db.Updateable(template).ExecuteCommandAsync().ConfigureAwait(false);
    }
}
