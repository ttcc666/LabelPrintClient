using System.Data.Common;
using System.Data.SQLite;
using LabelPrintClient.Config;
using Npgsql;

namespace LabelPrintClient.Modules.Settings.Services;

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
            ? new SQLiteConnection(AppConfigService.NormalizeSqliteConnection(connectionString, createDirectory: true))
            : new NpgsqlConnection(connectionString);
    }
}
