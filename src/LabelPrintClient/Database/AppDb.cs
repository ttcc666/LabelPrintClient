using System.IO;
using LabelPrintClient.Config;
using SqlSugar;

namespace LabelPrintClient.Database;

public static class AppDb
{
    private static readonly AsyncLocal<SqlSugarClient?> CurrentTransactionClient = new();

    private static SqlSugarScope _scope = null!;

    public static SqlSugarClient Db => CurrentTransactionClient.Value ?? _scope.CopyNew();

    public static void Init(AppSettings settings)
    {
        var dbType = settings.RunMode == AppRunMode.LocalSqlite
            ? DbType.Sqlite
            : DbType.PostgreSQL;

        var connectionString = settings.RunMode == AppRunMode.LocalSqlite
            ? NormalizeSqliteConnection(settings.SqliteConnection)
            : settings.PostgreSqlConnection;

        _scope = new SqlSugarScope(new ConnectionConfig
        {
            DbType = dbType,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
        {
            if (!settings.EnableSqlLogging)
                return;

            // 1. 拦截 SQL 正在执行事件，美化并输出完整拼装好的 SQL 语句
            db.Aop.OnLogExecuting = (sql, pars) =>
            {
                try
                {
                    // 利用 SqlSugar 工具类将参数拼接进 SQL 语句中，生成完整的可执行 SQL 字符串
                    var fullSql = UtilMethods.GetSqlString(dbType, sql, pars);

                    var logText = $"\r\n==================== [SQL LOG EXECUTING] ====================\r\n" +
                                  $"[Time] : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
                                  $"[SQL]  :\r\n{fullSql}\r\n" +
                                  $"============================================================\r\n";

                    System.Diagnostics.Debug.WriteLine(logText);
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"[SQL Raw Error] {sql}");
                }
            };

            // 2. 拦截 SQL 执行完毕事件，记录并输出高精度的耗时监控
            db.Aop.OnLogExecuted = (sql, pars) =>
            {
                var logText = $"[SQL LOG EXECUTED] Time Elapsed: {db.Ado.SqlExecutionTime} | {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n";
                System.Diagnostics.Debug.WriteLine(logText);
            };
        });
    }

    public static void Close()
    {
        try
        {
            CurrentTransactionClient.Value?.Close();
            _scope?.Close();
        }
        catch
        {
        }
    }

    public static async Task UseTranAsync(Func<Task> operation)
    {
        if (CurrentTransactionClient.Value != null)
        {
            await operation().ConfigureAwait(false);
            return;
        }

        var db = _scope.CopyNew();
        CurrentTransactionClient.Value = db;
        try
        {
            await db.Ado.BeginTranAsync().ConfigureAwait(false);
            await operation().ConfigureAwait(false);
            await db.Ado.CommitTranAsync().ConfigureAwait(false);
        }
        catch
        {
            await db.Ado.RollbackTranAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            CurrentTransactionClient.Value = null;
            db.Close();
        }
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
