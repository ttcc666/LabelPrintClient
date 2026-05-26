using System.IO;
using LabelPrintClient.Config;
using LabelPrintClient.Database;

namespace LabelPrintClient.Tests.Infrastructure;

public sealed class TestDatabase : IDisposable
{
    private readonly string _rootPath;
    private readonly string _dbPath;

    private TestDatabase(string rootPath, string dbPath, AppSettings settings)
    {
        _rootPath = rootPath;
        _dbPath = dbPath;
        Settings = settings;
    }

    public AppSettings Settings { get; }

    public static TestDatabase Create()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "LabelPrintClient.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        var dbPath = Path.Combine(rootPath, "label_print_test.db");
        var settings = new AppSettings
        {
            RunMode = AppRunMode.LocalSqlite,
            SqliteConnection = $"DataSource={dbPath}",
            LocalTemplateFolder = Path.Combine(rootPath, "Templates"),
            OperatorName = "test",
            ConfirmBeforePrint = false,
            EnableSqlLogging = false
        };

        AppDb.Close();
        AppDb.Init(settings);
        DbInitializer.InitTables();

        return new TestDatabase(rootPath, dbPath, settings);
    }

    public void Dispose()
    {
        AppDb.Close();

        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }
        catch
        {
        }
    }
}
