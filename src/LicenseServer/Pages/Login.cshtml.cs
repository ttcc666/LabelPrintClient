using System.Security.Claims;
using LicenseServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public sealed class LoginModel : PageModel
{
    private readonly AdminAuthService _auth;

    public LoginModel(AdminAuthService auth)
    {
        _auth = auth;
    }

    [BindProperty]
    public string UserName { get; set; } = "System";

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _auth.ValidateAsync(UserName, Password);
        if (user == null)
        {
            ErrorMessage = "用户名或密码错误。";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new("DisplayName", user.DisplayName)
        };
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return RedirectToPage("/Index");
    }
}
