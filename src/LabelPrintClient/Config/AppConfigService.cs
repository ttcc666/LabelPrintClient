using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelPrintClient.Config;

public static class AppConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static AppSettings LoadOrCreateDefault()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
        {
            var defaultSettings = new AppSettings();
            Save(defaultSettings);
            return defaultSettings;
        }

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
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static string GetConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
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