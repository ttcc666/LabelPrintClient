using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelPrintClient.Config;

public static class AppConfigService
{
    private const string AppFolderName = "LabelPrintClient";
    private const string ConfigFileName = "appsettings.json";
    private const string DefaultSqliteFileName = "label_print.db";
    private const string DefaultStandaloneLicenseFileName = "license.json";
    private const string DataDirectoryEnvironmentVariable = "LABELPRINTCLIENT_DATA_DIR";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static bool Exists()
    {
        return File.Exists(GetConfigPath());
    }

    public static AppSettings LoadOrCreateDefault()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
        {
            return CreateDefault();
        }

        return LoadFromFile(path);
    }

    public static AppSettings LoadRequired()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
            throw new FileNotFoundException("机器配置文件不存在。", path);

        return LoadFromFile(path);
    }

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            SqliteConnection = CreateSqliteConnection(GetDefaultSqliteDatabasePath()),
            LocalTemplateFolder = GetDefaultTemplateFolder(),
            StandaloneLicenseFilePath = GetDefaultStandaloneLicenseFilePath()
        };
    }

    private static AppSettings LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        if (settings == null)
        {
            throw new Exception("配置文件读取失败：appsettings.json 内容为空或格式错误。");
        }

        return settings;
    }

    public static void Save(AppSettings settings)
    {
        var path = GetConfigPath();
        Directory.CreateDirectory(GetMachineDataDirectory());

        settings.SqliteConnection = NormalizeSqliteConnection(settings.SqliteConnection, createDirectory: true);
        settings.LocalTemplateFolder = ResolveTemplateFolder(settings.LocalTemplateFolder);
        Directory.CreateDirectory(settings.LocalTemplateFolder);

        if (!string.IsNullOrWhiteSpace(settings.StandaloneLicenseFilePath))
        {
            settings.StandaloneLicenseFilePath = ResolveStandaloneLicenseFilePath(settings.StandaloneLicenseFilePath);
            var licenseDirectory = Path.GetDirectoryName(settings.StandaloneLicenseFilePath);
            if (!string.IsNullOrWhiteSpace(licenseDirectory))
                Directory.CreateDirectory(licenseDirectory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static string GetConfigPath()
    {
        return Path.Combine(GetMachineDataDirectory(), ConfigFileName);
    }

    public static string GetMachineDataDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppFolderName);
    }

    public static string GetDefaultDataDirectory()
    {
        return Path.Combine(GetMachineDataDirectory(), "Data");
    }

    public static string GetDefaultSqliteDatabasePath()
    {
        return Path.Combine(GetDefaultDataDirectory(), DefaultSqliteFileName);
    }

    public static string GetDefaultTemplateFolder()
    {
        return Path.Combine(GetMachineDataDirectory(), "Templates");
    }

    public static string GetDefaultBackupDirectory()
    {
        return Path.Combine(GetMachineDataDirectory(), "Backups");
    }

    public static string GetDefaultStandaloneLicenseFilePath()
    {
        return Path.Combine(GetMachineDataDirectory(), DefaultStandaloneLicenseFileName);
    }

    public static string CreateSqliteConnection(string databasePath)
    {
        return $"DataSource={databasePath}";
    }

    public static string NormalizeSqliteConnection(string connectionString, bool createDirectory = false)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            var kv = parts[i].Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
                continue;

            if (!IsSqliteDataSourceKey(kv[0]))
                continue;

            var value = ResolveMachinePath(kv[1]);
            var dir = Path.GetDirectoryName(value);
            if (createDirectory && !string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            parts[i] = $"{kv[0]}={value}";
            return string.Join(';', parts);
        }

        throw new InvalidOperationException("SQLite 连接串缺少 DataSource。");
    }

    public static string GetSqliteDatabasePath(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && IsSqliteDataSourceKey(kv[0]))
                return ResolveMachinePath(kv[1]);
        }

        throw new InvalidOperationException("SQLite 连接串缺少 DataSource。");
    }

    public static string ResolveTemplateFolder(string folder)
    {
        return ResolveMachinePath(folder);
    }

    public static string ResolveStandaloneLicenseFilePath(string path)
    {
        return ResolveMachinePath(path);
    }

    public static string ResolveMachinePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(GetMachineDataDirectory(), path);
    }

    private static bool IsSqliteDataSourceKey(string key)
    {
        return key.Equals("DataSource", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("Data Source", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // 支持 "RunMode": "LocalSqlite" / "LanPostgreSql" 这种字符串枚举写法。
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
