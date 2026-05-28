using LicenseServer.Models;

namespace LicenseServer.Infrastructure;

public static class DbInitializer
{
    public static void InitTables(LicenseDb db)
    {
        db.Db.CodeFirst.InitTables<Customer>();
        db.Db.CodeFirst.InitTables<AppLicense>();
        db.Db.CodeFirst.InitTables<OnlineSession>();
        db.Db.CodeFirst.InitTables<SigningKey>();
        db.Db.CodeFirst.InitTables<AdminUser>();
    }
}
