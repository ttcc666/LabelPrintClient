using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using LabelPrintClient.Config;
using Npgsql;

namespace LabelPrintClient.Services.Config;

public static class ConnectionTestService
{
    public static async Task TestAsync(AppRunMode runMode, string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("连接串不能为空。");

        await using var connection = CreateConnection(runMode, connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbConnection CreateConnection(AppRunMode runMode, string connectionString)
    {
        return runMode == AppRunMode.LocalSqlite
            ? new SQLiteConnection(NormalizeSqliteConnection(connectionString))
            : new NpgsqlConnection(connectionString);
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
                !key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Path.IsPathRooted(value))
                value = Path.Combine(AppContext.BaseDirectory, value);

            var dir = Path.GetDirectoryName(value);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            parts[i] = $"{key}={value}";
            break;
        }

        return string.Join(';', parts);
    }
}
