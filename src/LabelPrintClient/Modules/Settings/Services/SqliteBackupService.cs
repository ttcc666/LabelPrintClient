using System.IO;
using System.IO.Compression;
using System.Data.SQLite;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelPrintClient.Config;
using LabelPrintClient.Database;

namespace LabelPrintClient.Modules.Settings.Services;

public sealed class SqliteRestoreResult
{
    public string PreRestoreBackupPath { get; init; } = string.Empty;
}

public static class SqliteBackupService
{
    private const string ManifestEntryName = "manifest.json";
    private const string AppName = "LabelPrintClient";
    private const string BackupType = "LocalSqlite";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetDefaultBackupFileName()
    {
        return $"LabelPrintClient-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    public static Task CreateBackupAsync(string backupPath, AppSettings settings, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CreateBackupCore(backupPath, settings, cancellationToken), cancellationToken);
    }

    public static Task<SqliteRestoreResult> RestoreAsync(string backupPath, AppSettings settings, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => RestoreCore(backupPath, settings, cancellationToken), cancellationToken);
    }

    private static void CreateBackupCore(string backupPath, AppSettings settings, CancellationToken cancellationToken)
    {
        EnsureLocalSqlite(settings);

        var databasePath = AppConfigService.GetSqliteDatabasePath(settings.SqliteConnection);
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("SQLite 数据库文件不存在。", databasePath);

        var backupDirectory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(backupDirectory))
            Directory.CreateDirectory(backupDirectory);

        if (File.Exists(backupPath))
            File.Delete(backupPath);

        var configPath = AppConfigService.GetConfigPath();
        var templateFolder = AppConfigService.ResolveTemplateFolder(settings.LocalTemplateFolder);
        var hasTemplateFolder = Directory.Exists(templateFolder);
        var manifest = new BackupManifest
        {
            AppName = AppName,
            BackupType = BackupType,
            CreatedAt = DateTime.Now,
            DatabaseFileName = Path.GetFileName(databasePath),
            ConfigIncluded = File.Exists(configPath),
            TemplateFolderIncluded = hasTemplateFolder
        };

        var databaseSnapshotPath = Path.Combine(Path.GetTempPath(), $"LabelPrintClientBackup_{Guid.NewGuid():N}.db");
        try
        {
            CreateSqliteSnapshot(settings.SqliteConnection, databaseSnapshotPath);

            using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
            AddTextEntry(archive, ManifestEntryName, JsonSerializer.Serialize(manifest, JsonOptions));
            AddFile(archive, databaseSnapshotPath, $"database/{Path.GetFileName(databasePath)}", cancellationToken);

            if (File.Exists(configPath))
                AddFile(archive, configPath, "config/appsettings.json", cancellationToken);

            if (hasTemplateFolder)
                AddDirectory(archive, templateFolder, "templates", cancellationToken);
        }
        finally
        {
            TryDeleteFile(databaseSnapshotPath);
        }
    }

    private static SqliteRestoreResult RestoreCore(string backupPath, AppSettings settings, CancellationToken cancellationToken)
    {
        EnsureLocalSqlite(settings);

        if (!File.Exists(backupPath))
            throw new FileNotFoundException("备份文件不存在。", backupPath);

        var tempFolder = Path.Combine(Path.GetTempPath(), $"LabelPrintClientRestore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);

        try
        {
            ZipFile.ExtractToDirectory(backupPath, tempFolder);
            var manifest = ReadAndValidateManifest(tempFolder);
            var databaseBackupPath = ResolveBackupDatabasePath(tempFolder, manifest);

            var preRestoreBackupPath = Path.Combine(
                AppConfigService.GetDefaultBackupDirectory(),
                $"pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            CreateBackupCore(preRestoreBackupPath, settings, cancellationToken);

            AppDb.Close();

            var targetDatabasePath = AppConfigService.GetSqliteDatabasePath(settings.SqliteConnection);
            var targetDatabaseDirectory = Path.GetDirectoryName(targetDatabasePath);
            if (!string.IsNullOrWhiteSpace(targetDatabaseDirectory))
                Directory.CreateDirectory(targetDatabaseDirectory);
            File.Copy(databaseBackupPath, targetDatabasePath, true);

            var configBackupPath = Path.Combine(tempFolder, "config", "appsettings.json");
            var restoredTemplateFolder = settings.LocalTemplateFolder;
            if (File.Exists(configBackupPath))
            {
                restoredTemplateFolder = TryReadTemplateFolder(configBackupPath) ?? restoredTemplateFolder;
                File.Copy(configBackupPath, AppConfigService.GetConfigPath(), true);
            }

            var templateBackupFolder = Path.Combine(tempFolder, "templates");
            if (Directory.Exists(templateBackupFolder))
                ReplaceDirectory(templateBackupFolder, AppConfigService.ResolveTemplateFolder(restoredTemplateFolder));

            return new SqliteRestoreResult
            {
                PreRestoreBackupPath = preRestoreBackupPath
            };
        }
        finally
        {
            TryDeleteDirectory(tempFolder);
        }
    }

    private static BackupManifest ReadAndValidateManifest(string tempFolder)
    {
        var manifestPath = Path.Combine(tempFolder, ManifestEntryName);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("备份包缺少 manifest.json，不能恢复。");

        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidOperationException("备份包 manifest.json 格式无效。");

        if (!string.Equals(manifest.AppName, AppName, StringComparison.Ordinal) ||
            !string.Equals(manifest.BackupType, BackupType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("备份包不是当前应用的 LocalSqlite 备份。");
        }

        return manifest;
    }

    private static string ResolveBackupDatabasePath(string tempFolder, BackupManifest manifest)
    {
        var databaseFolder = Path.Combine(tempFolder, "database");
        var databasePath = Path.Combine(databaseFolder, manifest.DatabaseFileName);
        if (File.Exists(databasePath))
            return databasePath;

        var fallback = Directory.Exists(databaseFolder)
            ? Directory.EnumerateFiles(databaseFolder, "*.db").FirstOrDefault()
            : null;

        return fallback ?? throw new InvalidOperationException("备份包缺少 SQLite 数据库文件。");
    }

    private static void EnsureLocalSqlite(AppSettings settings)
    {
        if (settings.RunMode != AppRunMode.LocalSqlite)
            throw new InvalidOperationException("一键备份恢复仅支持 LocalSqlite 模式。");
    }

    private static string? TryReadTemplateFolder(string configPath)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(configPath), options);
            return string.IsNullOrWhiteSpace(settings?.LocalTemplateFolder)
                ? null
                : settings.LocalTemplateFolder;
        }
        catch
        {
            return null;
        }
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static void AddFile(ZipArchive archive, string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = entry.Open();
        source.CopyTo(target);
    }

    private static void AddDirectory(ZipArchive archive, string sourceFolder, string entryRoot, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceFolder, file).Replace('\\', '/');
            AddFile(archive, file, $"{entryRoot}/{relativePath}", cancellationToken);
        }
    }

    private static void CreateSqliteSnapshot(string connectionString, string snapshotPath)
    {
        if (File.Exists(snapshotPath))
            File.Delete(snapshotPath);

        using var source = new SQLiteConnection(AppConfigService.NormalizeSqliteConnection(connectionString, createDirectory: true));
        using var destination = new SQLiteConnection($"Data Source={snapshotPath};Version=3;");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination, "main", "main", -1, null, 0);
    }

    private static void ReplaceDirectory(string sourceFolder, string targetFolder)
    {
        EnsureSafeDirectoryTarget(targetFolder);
        if (Directory.Exists(targetFolder))
            Directory.Delete(targetFolder, true);

        CopyDirectory(sourceFolder, targetFolder);
    }

    private static void CopyDirectory(string sourceFolder, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        foreach (var directory in Directory.EnumerateDirectories(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFolder, directory);
            Directory.CreateDirectory(Path.Combine(targetFolder, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFolder, file);
            var targetPath = Path.Combine(targetFolder, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
                Directory.CreateDirectory(targetDirectory);
            File.Copy(file, targetPath, true);
        }
    }

    private static void EnsureSafeDirectoryTarget(string targetFolder)
    {
        var fullPath = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var machineDataDirectory = Path.GetFullPath(AppConfigService.GetMachineDataDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(fullPath) ||
            string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, baseDirectory, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, machineDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("本地模板目录指向高风险路径，已阻止自动恢复模板目录。");
        }
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed class BackupManifest
    {
        public string AppName { get; set; } = string.Empty;

        public string BackupType { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public string DatabaseFileName { get; set; } = string.Empty;

        public bool ConfigIncluded { get; set; }

        public bool TemplateFolderIncluded { get; set; }
    }
}
