using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Models;
using Stimulsoft.Report;
using System.IO;

namespace LabelPrintClient.Services.TemplateStorage;

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

    public void SaveReport(LabelTemplate template, StiReport report)
    {
        using var ms = new MemoryStream();
        report.Save(ms);

        var bytes = ms.ToArray();
        template.TemplateContent = bytes;
        template.TemplateFileName = string.IsNullOrWhiteSpace(template.TemplateFileName) ? $"{template.Name}.mrt" : template.TemplateFileName;
        template.TemplateHash = FileHashHelper.GetSha256(bytes);
        template.UpdateTime = DateTime.Now;

        AppDb.Db.Updateable(template).ExecuteCommand();
    }
}
