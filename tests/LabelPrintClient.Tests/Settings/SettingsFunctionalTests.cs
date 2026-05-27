using System.IO.Compression;
using LabelPrintClient.Config;
using LabelPrintClient.Database;
using LabelPrintClient.Modules.Settings.Services;
using LabelPrintClient.Modules.Template.Models;
using LabelPrintClient.Tests.Infrastructure;
using Directory = System.IO.Directory;
using File = System.IO.File;
using FileNotFoundException = System.IO.FileNotFoundException;
using Path = System.IO.Path;

namespace LabelPrintClient.Tests.Settings;

public class SettingsFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task ConnectionTestService_LocalSqlite_CreatesDatabaseAndExecutesProbe()
    {
        using var temp = new TempFolder();
        var dbPath = Path.Combine(temp.Path, "connection-test.db");

        await ConnectionTestService.TestAsync(AppRunMode.LocalSqlite, $"DataSource={dbPath}");

        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task ConnectionTestService_EmptyConnectionString_ThrowsReadableError()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConnectionTestService.TestAsync(AppRunMode.LocalSqlite, string.Empty));

        Assert.Contains("连接串不能为空", ex.Message);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SqliteBackupService_CreateBackup_IncludesManifestDatabaseAndTemplates()
    {
        using var database = TestDatabase.Create();
        var templateFolder = database.Settings.LocalTemplateFolder;
        Directory.CreateDirectory(templateFolder);
        await File.WriteAllTextAsync(Path.Combine(templateFolder, "sample.mrt"), "template");
        var category = await TestSeed.CategoryAsync();
        await TestSeed.TemplateAsync(category.Id, Modules.Template.Models.LabelTemplateMode.Normal);
        using var temp = new TempFolder();
        var backupPath = Path.Combine(temp.Path, "backup.zip");

        await SqliteBackupService.CreateBackupAsync(backupPath, database.Settings);

        using var archive = ZipFile.OpenRead(backupPath);
        var entries = archive.Entries.Select(x => x.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("manifest.json", entries);
        Assert.Contains(entries, x => x.StartsWith("database/", StringComparison.OrdinalIgnoreCase) && x.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("templates/sample.mrt", entries);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SqliteBackupService_WhenRunModeIsNotLocalSqlite_ThrowsReadableError()
    {
        var settings = new AppSettings
        {
            RunMode = AppRunMode.LanPostgreSql,
            PostgreSqlConnection = "Host=127.0.0.1;Database=test;"
        };
        using var temp = new TempFolder();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqliteBackupService.CreateBackupAsync(Path.Combine(temp.Path, "backup.zip"), settings));

        Assert.Contains("仅支持 LocalSqlite", ex.Message);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SqliteBackupService_WhenBackupFileMissing_ThrowsReadableError()
    {
        using var database = TestDatabase.Create();
        using var temp = new TempFolder();
        var missingPath = Path.Combine(temp.Path, "missing.zip");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            SqliteBackupService.RestoreAsync(missingPath, database.Settings));

        Assert.Contains("备份文件不存在", ex.Message);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public void AppConfigService_SaveThenLoad_PreservesSettings()
    {
        var configPath = AppConfigService.GetConfigPath();
        var hadOriginal = File.Exists(configPath);
        var original = hadOriginal ? File.ReadAllText(configPath) : null;
        try
        {
            var settings = new AppSettings
            {
                RunMode = AppRunMode.LocalSqlite,
                SqliteConnection = "DataSource=Data/test.db",
                LocalTemplateFolder = "TestTemplates",
                OperatorName = "tester",
                DefaultPrinterName = "Printer-01",
                DefaultPrintCopies = 3,
                ConfirmBeforePrint = false,
                EnableSqlLogging = true,
                ThemeMode = AppThemeMode.Dark,
                Language = AppLanguage.EnUs
            };

            AppConfigService.Save(settings);
            var loaded = AppConfigService.LoadOrCreateDefault();

            Assert.Equal(AppRunMode.LocalSqlite, loaded.RunMode);
            Assert.Equal("DataSource=Data/test.db", loaded.SqliteConnection);
            Assert.Equal("TestTemplates", loaded.LocalTemplateFolder);
            Assert.Equal("tester", loaded.OperatorName);
            Assert.Equal("Printer-01", loaded.DefaultPrinterName);
            Assert.Equal(3, loaded.DefaultPrintCopies);
            Assert.False(loaded.ConfirmBeforePrint);
            Assert.True(loaded.EnableSqlLogging);
            Assert.Equal(AppThemeMode.Dark, loaded.ThemeMode);
            Assert.Equal(AppLanguage.EnUs, loaded.Language);
        }
        finally
        {
            if (hadOriginal)
                File.WriteAllText(configPath, original);
            else if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SqliteBackupService_Restore_ReplacesDatabaseAndTemplateFolder()
    {
        using var database = TestDatabase.Create();
        var templateFolder = database.Settings.LocalTemplateFolder;
        Directory.CreateDirectory(templateFolder);
        await File.WriteAllTextAsync(Path.Combine(templateFolder, "sample.mrt"), "original");
        AppConfigService.Save(database.Settings);
        var category = await TestSeed.CategoryAsync("备份分类");
        await TestSeed.TemplateAsync(category.Id, LabelTemplateMode.Normal, name: "备份模板");
        using var temp = new TempFolder();
        var backupPath = Path.Combine(temp.Path, "backup.zip");
        await SqliteBackupService.CreateBackupAsync(backupPath, database.Settings);

        await AppDb.Db.Deleteable<LabelTemplate>().ExecuteCommandAsync();
        await AppDb.Db.Deleteable<LabelCategory>().ExecuteCommandAsync();
        await File.WriteAllTextAsync(Path.Combine(templateFolder, "sample.mrt"), "changed");

        var result = await SqliteBackupService.RestoreAsync(backupPath, database.Settings);
        AppDb.Init(database.Settings);

        var restoredCategory = await AppDb.Db.Queryable<LabelCategory>().SingleAsync();
        var restoredTemplate = await AppDb.Db.Queryable<LabelTemplate>().SingleAsync();
        var restoredTemplateText = await File.ReadAllTextAsync(Path.Combine(templateFolder, "sample.mrt"));

        Assert.True(File.Exists(result.PreRestoreBackupPath));
        Assert.Equal("备份分类", restoredCategory.Name);
        Assert.Equal("备份模板", restoredTemplate.Name);
        Assert.Equal("original", restoredTemplateText);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LabelPrintClient.Tests", Guid.NewGuid().ToString("N"));

        public TempFolder()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
