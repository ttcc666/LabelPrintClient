namespace LabelPrintClient.Config;

public class AppSettings
{
    public AppRunMode RunMode { get; set; } = AppRunMode.LocalSqlite;

    public string SqliteConnection { get; set; } = "DataSource=Data/label_print.db";

    public string PostgreSqlConnection { get; set; } = "Host=127.0.0.1;Port=5432;Username=postgres;Database=label_print;";

    public string LocalTemplateFolder { get; set; } = "Templates";

    public string OperatorName { get; set; } = "admin";
}
