using LicenseServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public sealed class SetupModel : PageModel
{
    private readonly AdminAuthService _auth;

    public SetupModel(AdminAuthService auth)
    {
        _auth = auth;
    }

    [BindProperty]
    public string DisplayName { get; set; } = "Administrator";

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        return await _auth.HasAnyAdminAsync() ? RedirectToPage("/Login") : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await _auth.HasAnyAdminAsync())
            return RedirectToPage("/Login");
        if (Password.Length < 6)
        {
            ErrorMessage = "密码至少 6 位。";
            return Page();
        }

        await _auth.CreateInitialAdminAsync(Password, DisplayName);
        return RedirectToPage("/Login");
    }
}
