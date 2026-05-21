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

    public void SaveReport(LabelTemplate template, StiReport report)
    {
        if (string.IsNullOrWhiteSpace(template.TemplatePath))
            throw new InvalidOperationException("模板文件路径为空，无法保存。");

        var dir = Path.GetDirectoryName(template.TemplatePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        report.Save(template.TemplatePath);
        template.TemplateFileName = string.IsNullOrWhiteSpace(template.TemplateFileName)
            ? Path.GetFileName(template.TemplatePath)
            : template.TemplateFileName;
        template.TemplateHash = File.Exists(template.TemplatePath) ? FileHashHelper.GetSha256(template.TemplatePath) : null;
        template.UpdateTime = DateTime.Now;

        AppDb.Db.Updateable(template).ExecuteCommand();
    }
}
