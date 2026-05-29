using LicenseServer.Infrastructure;
using LicenseServer.Models;
using LicenseServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public class IndexModel : PageModel
{
    private readonly LicenseDb _db;
    private readonly SigningKeyService _signingKeys;

    public IndexModel(LicenseDb db, SigningKeyService signingKeys)
    {
        _db = db;
        _signingKeys = signingKeys;
    }

    public int CustomerCount { get; private set; }
    public int LicenseCount { get; private set; }
    public int SessionCount { get; private set; }
    public bool HasSigningKey { get; private set; }

    public async Task OnGetAsync()
    {
        CustomerCount = await _db.Db.Queryable<Customer>().CountAsync();
        LicenseCount = await _db.Db.Queryable<AppLicense>().CountAsync();
        SessionCount = await _db.Db.Queryable<OnlineSession>().CountAsync(x => x.ExpireTime > DateTime.Now);
        HasSigningKey = await _signingKeys.GetActiveAsync() != null;
    }
}
