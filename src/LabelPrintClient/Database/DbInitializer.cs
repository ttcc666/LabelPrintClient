using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Database;

public static class DbInitializer
{
    public static void InitTables()
    {
        // SqlSugarCore's generic InitTables overload count varies by package version.
        // Initializing one table per call is the most compatible approach for SQLite and PostgreSQL.
        AppDb.Db.CodeFirst.InitTables<LabelCategory>();
        AppDb.Db.CodeFirst.InitTables<LabelTemplate>();
        AppDb.Db.CodeFirst.InitTables<LabelTemplateField>();
        AppDb.Db.CodeFirst.InitTables<LabelImportBatch>();
        AppDb.Db.CodeFirst.InitTables<LabelImportRow>();
        AppDb.Db.CodeFirst.InitTables<LabelPrintJob>();
        AppDb.Db.CodeFirst.InitTables<LabelPrintJobRow>();
    }
}
