using LabelPrintClient.Services;

namespace LabelPrintClient.Modules.Template.Models;

public static class TemplateSystemFields
{
    public const string BatchNo = "$batch_no";
    public const string SerialNo = "$serial_no";

    public static bool IsSystemField(string? fieldCode)
    {
        return string.Equals(fieldCode, BatchNo, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldCode, SerialNo, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsManagedSystemField(string? fieldCode)
    {
        return string.Equals(fieldCode, BatchNo, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fieldCode, SerialNo, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDisplayName(string fieldCode)
    {
        if (string.Equals(fieldCode, BatchNo, StringComparison.OrdinalIgnoreCase))
            return AppLanguageService.GetString("SystemField.BatchNo");
        if (string.Equals(fieldCode, SerialNo, StringComparison.OrdinalIgnoreCase))
            return AppLanguageService.GetString("SystemField.SerialNo");
        return fieldCode;
    }
}
