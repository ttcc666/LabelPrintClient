using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.ViewModels;

public class PrintJobGridItem
{
    public long Id { get; set; }

    public long TemplateId { get; set; }

    public long BatchId { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public int SelectedRowCount { get; set; }

    public string? PrinterName { get; set; }

    public string PrinterNameText => string.IsNullOrWhiteSpace(PrinterName) ? "默认打印机" : PrinterName;

    public string Status { get; set; } = string.Empty;

    public string StatusText => Status switch
    {
        "Printed" => "已打印",
        "Failed" => "失败",
        "Printing" => "打印中",
        "Preview" => "预览",
        _ => Status
    };

    public string? OperatorName { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime? PrintTime { get; set; }

    public string? ErrorMessage { get; set; }

    public static PrintJobGridItem From(LabelPrintJob job)
    {
        return new PrintJobGridItem
        {
            Id = job.Id,
            TemplateId = job.TemplateId,
            BatchId = job.BatchId,
            TemplateName = job.TemplateName,
            SelectedRowCount = job.SelectedRowCount,
            PrinterName = job.PrinterName,
            Status = job.Status,
            OperatorName = job.OperatorName,
            CreateTime = job.CreateTime,
            PrintTime = job.PrintTime,
            ErrorMessage = job.ErrorMessage
        };
    }
}

