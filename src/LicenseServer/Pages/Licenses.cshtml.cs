using System.Text;
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

    public LicensesModel(LicenseDb db, StandaloneLicenseGenerator generator)
    {
        _db = db;
        _generator = generator;
    }

    public List<Customer> Customers { get; private set; } = new();
    public List<LicenseRow> Items { get; private set; } = new();
    public string ErrorMessage { get; private set; } = string.Empty;

    [BindProperty]
    public LicenseInput Input { get; set; } = new() { ProductCode = "LABEL_PRINT_CLIENT", ExpireTime = DateTime.Today.AddYears(1), TotalCount = 1, IssuedTo = "Customer" };

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

    public async Task<IActionResult> OnGetDownloadAsync(long id)
    {
        var license = await _db.Db.Queryable<AppLicense>().FirstAsync(x => x.Id == id);
        if (license == null)
            return NotFound();

        var json = await _generator.GenerateAsync(license);
        var bytes = Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", $"{license.ProductCode}-{license.MachineCode}.license");
    }

    private async Task LoadAsync()
    {
        Customers = await _db.Db.Queryable<Customer>().Where(x => x.IsEnabled).OrderBy(x => x.Name).ToListAsync();
        var licenses = await _db.Db.Queryable<AppLicense>().OrderByDescending(x => x.CreateTime).ToListAsync();
        var customerMap = (await _db.Db.Queryable<Customer>().ToListAsync()).ToDictionary(x => x.Id, x => x.Name);
        Items = licenses.Select(x => new LicenseRow(
            x,
            customerMap.GetValueOrDefault(x.CustomerId, "-"),
            x.AccessKeyHash == null ? "-" : "创建后仅显示一次")).ToList();
        if (TempData.TryGetValue("AccessKey", out var accessKey))
            ErrorMessage = accessKey?.ToString() ?? string.Empty;
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
