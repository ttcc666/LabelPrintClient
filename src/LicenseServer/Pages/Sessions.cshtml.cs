using LicenseServer.Infrastructure;
using LicenseServer.Models;
using LicenseServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public sealed class SessionsModel : PageModel
{
    private readonly LicenseDb _db;
    private readonly FloatingLicenseService _floating;

    public SessionsModel(LicenseDb db, FloatingLicenseService floating)
    {
        _db = db;
        _floating = floating;
    }

    public List<SessionRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var sessions = await _db.Db.Queryable<OnlineSession>().OrderByDescending(x => x.LastHeartbeat).ToListAsync();
        var licenses = (await _db.Db.Queryable<AppLicense>().ToListAsync()).ToDictionary(x => x.Id, x => x.ProductCode);
        Items = sessions.Select(x => new SessionRow(x, licenses.GetValueOrDefault(x.LicenseId, "-"))).ToList();
    }

    public async Task<IActionResult> OnPostReleaseAsync(long id)
    {
        await _floating.ForceReleaseAsync(id);
        return RedirectToPage();
    }

    public sealed record SessionRow(OnlineSession Session, string ProductCode);
}
