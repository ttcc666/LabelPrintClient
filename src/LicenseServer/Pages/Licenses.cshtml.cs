using System.Text;
using System.Text.Json;
using LicenseServer.Infrastructure;
using LicenseServer.Models;
using LicenseServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public sealed class LicensesModel : PageModel
{
    private readonly LicenseDb _db;
    private readonly StandaloneLicenseGenerator _generator;
    private readonly SigningKeyService _signingKeys;
    private readonly LicenseValidator _validator;
    private readonly PrivateKeyProtector _protector;

    public LicensesModel(
        LicenseDb db,
        StandaloneLicenseGenerator generator,
        SigningKeyService signingKeys,
        LicenseValidator validator,
        PrivateKeyProtector protector)
    {
        _db = db;
        _generator = generator;
        _signingKeys = signingKeys;
        _validator = validator;
        _protector = protector;
    }

    public List<Customer> Customers { get; private set; } = new();
    public List<LicenseRow> Items { get; private set; } = new();
    public string ErrorMessage { get; private set; } = string.Empty;

    public bool HasMasterKey { get; private set; }
    public bool HasActiveKey { get; private set; }

    [BindProperty]
    public LicenseInput Input { get; set; } = new() { ProductCode = "LABEL_PRINT_CLIENT", ExpireTime = DateTime.Today.AddYears(1), TotalCount = 1, IssuedTo = "Customer" };

    [BindProperty]
    public string? ImportLicenseContent { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input.CustomerId == 0 || string.IsNullOrWhiteSpace(Input.ProductCode))
        {
            ErrorMessage = "客户和产品编码不能为空。";
            await LoadAsync();
            return Page();
        }

        if (Input.LicenseMode == LicenseMode.Standalone && string.IsNullOrWhiteSpace(Input.MachineCode))
        {
            ErrorMessage = "单机授权必须填写机器码。";
            await LoadAsync();
            return Page();
        }

        var accessKey = Input.LicenseMode == LicenseMode.Floating ? HashService.NewSecret(24) : null;
        var license = new AppLicense
        {
            Id = IdHelper.NewId(),
            CustomerId = Input.CustomerId,
            ProductCode = Input.ProductCode.Trim(),
            LicenseMode = Input.LicenseMode,
            ExpireTime = Input.ExpireTime.Date.AddDays(1).AddTicks(-1),
            IsEnabled = true,
            MachineCode = string.IsNullOrWhiteSpace(Input.MachineCode) ? null : Input.MachineCode.Trim(),
            TotalCount = Math.Max(1, Input.TotalCount),
            AccessKeyHash = accessKey == null ? null : HashService.Sha256(accessKey),
            IssuedTo = Input.IssuedTo.Trim(),
            CreateTime = DateTime.Now
        };
        await _db.Db.Insertable(license).ExecuteCommandAsync();
        TempData["AccessKey"] = accessKey == null ? null : $"新浮动许可证 AccessKey：{accessKey}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportLicenseContent))
        {
            ErrorMessage = "导入内容不能为空。";
            await LoadAsync();
            return Page();
        }

        LicenseDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<LicenseDocument>(ImportLicenseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            ErrorMessage = "许可证格式无效，反序列化失败。";
            await LoadAsync();
            return Page();
        }

        if (doc == null)
        {
            ErrorMessage = "反序列化许可证为空。";
            await LoadAsync();
            return Page();
        }

        if (!_validator.Verify(doc))
        {
            ErrorMessage = "许可证签名验证失败，不是公司合法的签名文件。";
            await LoadAsync();
            return Page();
        }

        if (doc.LicenseMode != LicenseMode.Floating)
        {
            ErrorMessage = "局域网 LicenseServer 仅支持导入浮动授权（Floating）。";
            await LoadAsync();
            return Page();
        }

        if (doc.ExpireTime <= DateTime.Now)
        {
            ErrorMessage = "该许可证已过期，无法导入。";
            await LoadAsync();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(doc.AccessKey))
        {
            ErrorMessage = "导入的浮动许可证缺少 AccessKey。";
            await LoadAsync();
            return Page();
        }

        var customerName = doc.IssuedTo.Trim();
        var customer = await _db.Db.Queryable<Customer>().FirstAsync(x => x.Name == customerName);
        if (customer == null)
        {
            customer = new Customer
            {
                Id = IdHelper.NewId(),
                Name = customerName,
                IsEnabled = true,
                CreateTime = DateTime.Now
            };
            await _db.Db.Insertable(customer).ExecuteCommandAsync();
        }

        var licenseId = doc.Id ?? IdHelper.NewId();
        var existingLicense = await _db.Db.Queryable<AppLicense>().FirstAsync(x => x.Id == licenseId);

        var license = new AppLicense
        {
            Id = licenseId,
            CustomerId = customer.Id,
            ProductCode = doc.ProductCode.Trim(),
            LicenseMode = LicenseMode.Floating,
            ExpireTime = doc.ExpireTime,
            IsEnabled = true,
            MachineCode = null,
            TotalCount = doc.TotalCount,
            AccessKeyHash = HashService.Sha256(doc.AccessKey),
            IssuedTo = customerName,
            CreateTime = existingLicense?.CreateTime ?? DateTime.Now
        };

        if (existingLicense != null)
        {
            await _db.Db.Updateable(license).ExecuteCommandAsync();
            ErrorMessage = "导入成功：已成功覆盖并更新现有许可证记录。";
        }
        else
        {
            await _db.Db.Insertable(license).ExecuteCommandAsync();
            ErrorMessage = "导入成功：已成功创建新浮动许可证。";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(long id)
    {
        var license = await _db.Db.Queryable<AppLicense>().FirstAsync(x => x.Id == id);
        if (license == null)
            return NotFound();

        if (license.LicenseMode != LicenseMode.Standalone)
        {
            TempData["ErrorMessage"] = "非法请求：单机版证书方可直接下载。";
            return RedirectToPage();
        }

        try
        {
            var json = await _generator.GenerateAsync(license);
            var bytes = Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"{license.ProductCode}-{license.MachineCode}.license");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            TempData["ErrorMessage"] = $"数字签名生成崩溃：当前数据库中激活的私钥无效，无法完成签名。请前往【签名密钥 (Signing Key)】页面重新导入有效的私钥。详细错误：{ex.Message}";
            return RedirectToPage();
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostDownloadAsync(long id, string? accessKey = null)
    {
        var license = await _db.Db.Queryable<AppLicense>().FirstAsync(x => x.Id == id);
        if (license == null)
            return NotFound();

        if (license.LicenseMode != LicenseMode.Floating)
        {
            TempData["ErrorMessage"] = "非法请求：浮动证书下载需要通过 Post 安全校验验证。";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(accessKey))
        {
            TempData["ErrorMessage"] = "下载浮动许可证需要提供匹配的 AccessKey。";
            return RedirectToPage();
        }

        var hash = HashService.Sha256(accessKey.Trim());
        if (!string.Equals(license.AccessKeyHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "提供的 AccessKey 校验失败，无法下载。";
            return RedirectToPage();
        }

        try
        {
            var json = await _generator.GenerateAsync(license, accessKey.Trim());
            var bytes = Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"{license.ProductCode}-floating.license");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            TempData["ErrorMessage"] = $"数字签名生成崩溃：当前数据库中激活的私钥无效，无法完成签名。请前往【签名密钥 (Signing Key)】页面重新导入有效的私钥。详细错误：{ex.Message}";
            return RedirectToPage();
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage();
        }
    }

    private async Task LoadAsync()
    {
        HasMasterKey = _protector.HasMasterKey;
        HasActiveKey = await _signingKeys.GetActiveAsync() != null;
        Customers = await _db.Db.Queryable<Customer>().Where(x => x.IsEnabled).OrderBy(x => x.Name).ToListAsync();
        var licenses = await _db.Db.Queryable<AppLicense>().OrderByDescending(x => x.CreateTime).ToListAsync();
        var customerMap = (await _db.Db.Queryable<Customer>().ToListAsync()).ToDictionary(x => x.Id, x => x.Name);
        Items = licenses.Select(x => new LicenseRow(
            x,
            customerMap.GetValueOrDefault(x.CustomerId, "-"),
            x.AccessKeyHash == null ? "-" : "创建后仅显示一次")).ToList();

        if (TempData != null && TempData.TryGetValue("AccessKey", out var accessKey))
            ErrorMessage = accessKey?.ToString() ?? string.Empty;

        if (TempData != null && TempData.TryGetValue("ErrorMessage", out var errMsg))
            ErrorMessage = errMsg?.ToString() ?? string.Empty;
    }

    public sealed class LicenseInput
    {
        public long CustomerId { get; set; }
        public string ProductCode { get; set; } = "LABEL_PRINT_CLIENT";
        public LicenseMode LicenseMode { get; set; }
        public DateTime ExpireTime { get; set; } = DateTime.Today.AddYears(1);
        public string? MachineCode { get; set; }
        public int TotalCount { get; set; } = 1;
        public string IssuedTo { get; set; } = string.Empty;
    }

    public sealed record LicenseRow(AppLicense License, string CustomerName, string AccessKeyPreview);
}
