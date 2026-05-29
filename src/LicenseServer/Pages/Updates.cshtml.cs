using LicenseServer.Models;
using LicenseServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public sealed class UpdatesModel : PageModel
{
    private readonly UpdateReleaseService _updates;

    public UpdatesModel(UpdateReleaseService updates)
    {
        _updates = updates;
    }

    public List<UpdateRelease> Items { get; private set; } = new();

    public string Message { get; private set; } = string.Empty;

    [BindProperty]
    public UpdateReleaseInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ReleaseZip { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        if (ReleaseZip == null || ReleaseZip.Length == 0)
        {
            TempData["UpdateMessage"] = "请选择 Velopack release 目录 zip 包。";
            return RedirectToPage();
        }

        try
        {
            await using var stream = ReleaseZip.OpenReadStream();
            var release = await _updates.UploadAsync(new UpdateReleaseUploadRequest(
                Input.ProductCode,
                Input.Channel,
                Input.Version,
                Input.IsMandatory,
                Input.ReleaseNotes), stream, cancellationToken);

            TempData["UpdateMessage"] = $"上传成功：{release.ProductCode} / {release.Channel} / {release.Version}";
        }
        catch (Exception ex)
        {
            TempData["UpdateMessage"] = $"上传失败：{ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleEnabledAsync(long id, bool enabled)
    {
        await _updates.SetEnabledAsync(id, enabled);
        TempData["UpdateMessage"] = enabled ? "更新版本已启用。" : "更新版本已禁用。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleMandatoryAsync(long id, bool mandatory)
    {
        await _updates.SetMandatoryAsync(id, mandatory);
        TempData["UpdateMessage"] = mandatory ? "更新版本已标记为强制更新。" : "更新版本已取消强制更新。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, bool deleteFiles = false)
    {
        await _updates.DeleteAsync(id, deleteFiles);
        TempData["UpdateMessage"] = deleteFiles ? "更新版本和包文件已删除。" : "更新版本记录已删除。";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Items = await _updates.GetAllAsync();
        if (TempData.TryGetValue("UpdateMessage", out var value))
            Message = value?.ToString() ?? string.Empty;
    }

    public sealed class UpdateReleaseInput
    {
        public string ProductCode { get; set; } = "LABEL_PRINT_CLIENT";

        public string Channel { get; set; } = "stable";

        public string Version { get; set; } = "1.0.0";

        public bool IsMandatory { get; set; }

        public string? ReleaseNotes { get; set; }
    }
}
