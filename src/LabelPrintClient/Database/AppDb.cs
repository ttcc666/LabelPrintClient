using System.IO;
using LabelPrintClient.Config;
using SqlSugar;

namespace LabelPrintClient.Database;

public static class AppDb
{
    public static SqlSugarScope Db { get; private set; } = null!;

    public static void Init(AppSettings settings)
    {
        var dbType = settings.RunMode == AppRunMode.LocalSqlite
            ? DbType.Sqlite
            : DbType.PostgreSQL;

        var connectionString = settings.RunMode == AppRunMode.LocalSqlite
            ? NormalizeSqliteConnection(settings.SqliteConnection)
            : settings.PostgreSqlConnection;

        Db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = dbType,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
        {
            db.Aop.OnLogExecuting = (sql, pars) =>
            {
                System.Diagnostics.Debug.WriteLine(sql);
            };
        });
    }

    private static string NormalizeSqliteConnection(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            var kv = parts[i].Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;
            var key = kv[0];
            var value = kv[1];
            if (!key.Equals("DataSource", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)) continue;

            if (!Path.IsPathRooted(value))
            {
                value = Path.Combine(AppContext.BaseDirectory, value);
            }

            var dir = Path.GetDirectoryName(value);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            parts[i] = $"{key}={value}";
            break;
        }

        return string.Join(';', parts);
    }
}
