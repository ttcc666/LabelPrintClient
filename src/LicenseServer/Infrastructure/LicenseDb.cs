using System.IO;
using LicenseServer.Config;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace LicenseServer.Infrastructure;

public sealed class LicenseDb
{
    private readonly SqlSugarScope _scope;

    public LicenseDb(IOptions<LicenseServerOptions> options)
    {
        var settings = options.Value;
        var dbType = string.Equals(settings.RunMode, "PostgreSql", StringComparison.OrdinalIgnoreCase)
            ? DbType.PostgreSQL
            : DbType.Sqlite;
        var connectionString = dbType == DbType.Sqlite
            ? NormalizeSqliteConnection(settings.SqliteConnection)
            : settings.PostgreSqlConnection;

        _scope = new SqlSugarScope(new ConnectionConfig
        {
            DbType = dbType,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
    }

    public SqlSugarClient Db => _scope.CopyNew();

    private static string NormalizeSqliteConnection(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            var kv = parts[i].Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 ||
                (!kv[0].Equals("DataSource", StringComparison.OrdinalIgnoreCase) &&
                 !kv[0].Equals("Data Source", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var value = Path.IsPathRooted(kv[1]) ? kv[1] : Path.Combine(AppContext.BaseDirectory, kv[1]);
            var dir = Path.GetDirectoryName(value);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            parts[i] = $"{kv[0]}={value}";
            break;
        }

        return string.Join(';', parts);
    }
}
