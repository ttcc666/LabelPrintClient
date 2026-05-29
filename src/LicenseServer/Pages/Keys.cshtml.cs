using LicenseServer.Models;
using LicenseServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public sealed class KeysModel : PageModel
{
    private readonly SigningKeyService _signingKeys;
    private readonly PrivateKeyProtector _protector;

    public KeysModel(SigningKeyService signingKeys, PrivateKeyProtector protector)
    {
        _signingKeys = signingKeys;
        _protector = protector;
    }

    public SigningKey? ActiveKey { get; private set; }

    public bool HasMasterKey => _protector.HasMasterKey;

    [BindProperty]
    public string PrivateKeyPem { get; set; } = string.Empty;

    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        ActiveKey = await _signingKeys.GetActiveAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_protector.HasMasterKey)
        {
            ErrorMessage = "缺少 LICENSE_SERVER_MASTER_KEY，不能导入私钥。";
            ActiveKey = await _signingKeys.GetActiveAsync();
            return Page();
        }

        try
        {
            await _signingKeys.ImportAsync(PrivateKeyPem);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ActiveKey = await _signingKeys.GetActiveAsync();
            return Page();
        }
    }
}
