using LabelPrintClient.Config;

namespace LabelPrintClient.Services.TemplateStorage;

public static class LabelTemplateStorageFactory
{
    public static ILabelTemplateStorageService Create(AppRunMode mode)
    {
        return mode == AppRunMode.LocalSqlite
            ? new LocalFileTemplateStorageService()
            : new DatabaseTemplateStorageService();
    }
}
