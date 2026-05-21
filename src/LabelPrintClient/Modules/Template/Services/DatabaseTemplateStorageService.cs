using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using Stimulsoft.Report;
using System.IO;

namespace LabelPrintClient.Modules.Template.Services;

public class DatabaseTemplateStorageService : ILabelTemplateStorageService
{
    public StiReport LoadReport(LabelTemplate template)
    {
        if (template.TemplateContent == null || template.TemplateContent.Length == 0)
            throw new InvalidOperationException("数据库中的模板内容为空，请先上传或设计模板。");

        var report = new StiReport();
        using var ms = new MemoryStream(template.TemplateContent);
        report.Load(ms);
        return report;
    }

    public Task<StiReport> LoadReportAsync(LabelTemplate template, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => LoadReport(template), cancellationToken);
    }

    public async Task SaveReportAsync(LabelTemplate template, StiReport report, CancellationToken cancellationToken = default)
    {
        var bytes = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            report.Save(ms);
            return ms.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        template.TemplateContent = bytes;
        template.TemplateFileName = string.IsNullOrWhiteSpace(template.TemplateFileName) ? $"{template.Name}.mrt" : template.TemplateFileName;
        template.TemplateHash = FileHashHelper.GetSha256(bytes);
        template.UpdateTime = DateTime.Now;

        await AppDb.Db.Updateable(template).ExecuteCommandAsync().ConfigureAwait(false);
    }
}

