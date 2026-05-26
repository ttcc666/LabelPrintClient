using LabelPrintClient.Config;
using LabelPrintClient.Modules.Template.Services;

namespace LabelPrintClient.Tests.Template;

public class TemplateStorageFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public void StorageFactory_LocalSqlite_UsesLocalFileStorage()
    {
        var storage = LabelTemplateStorageFactory.Create(AppRunMode.LocalSqlite);

        Assert.IsType<LocalFileTemplateStorageService>(storage);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public void StorageFactory_PostgreSql_UsesDatabaseStorage()
    {
        var storage = LabelTemplateStorageFactory.Create(AppRunMode.LanPostgreSql);

        Assert.IsType<DatabaseTemplateStorageService>(storage);
    }
}
