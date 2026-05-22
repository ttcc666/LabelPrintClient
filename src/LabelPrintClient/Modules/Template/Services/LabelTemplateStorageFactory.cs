using LabelPrintClient.Config;

namespace LabelPrintClient.Modules.Template.Services;

public static class LabelTemplateStorageFactory
{
    public static ILabelTemplateStorageService Create(AppRunMode mode)
    {
        return mode == AppRunMode.LocalSqlite
            ? new LocalFileTemplateStorageService()
            : new DatabaseTemplateStorageService();
    }
}