using LicenseServer.Infrastructure;
using LicenseServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenseServer.Pages;

public sealed class CustomersModel : PageModel
{
    private readonly LicenseDb _db;

    public CustomersModel(LicenseDb db)
    {
        _db = db;
    }

    public List<Customer> Customers { get; private set; } = new();

    [BindProperty]
    public CustomerInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        Customers = await _db.Db.Queryable<Customer>().OrderByDescending(x => x.CreateTime).ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Name))
            return RedirectToPage();

        var customer = new Customer
        {
            Id = Input.Id == 0 ? IdHelper.NewId() : Input.Id,
            Name = Input.Name.Trim(),
            Contact = Input.Contact?.Trim(),
            Remark = Input.Remark?.Trim(),
            IsEnabled = Input.IsEnabled,
            CreateTime = DateTime.Now
        };

        await _db.Db.Storageable(customer).ExecuteCommandAsync();
        return RedirectToPage();
    }

    public sealed class CustomerInput
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Contact { get; set; }
        public string? Remark { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
