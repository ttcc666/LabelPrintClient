namespace LicenseServer.Config;

public sealed class LicenseServerOptions
{
    public string RunMode { get; set; } = "LocalSqlite";

    public string SqliteConnection { get; set; } = "DataSource=Data/license_server.db";

    public string PostgreSqlConnection { get; set; } = "Host=127.0.0.1;Port=5432;Username=postgres;Database=license_server;";

    public int HeartbeatIntervalSeconds { get; set; } = 30;

    public int SessionTimeoutSeconds { get; set; } = 120;

    public string MasterKeyEnvironmentName { get; set; } = "LICENSE_SERVER_MASTER_KEY";
}
