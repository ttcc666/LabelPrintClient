using LicenseServer.Api;
using LicenseServer.Config;
using LicenseServer.Infrastructure;
using LicenseServer.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LicenseServerOptions>(builder.Configuration.GetSection("LicenseServer"));
builder.Services.AddSingleton<LicenseDb>();
builder.Services.AddScoped<AdminAuthService>();
builder.Services.AddScoped<PrivateKeyProtector>();
builder.Services.AddScoped<SigningKeyService>();
builder.Services.AddScoped<StandaloneLicenseGenerator>();
builder.Services.AddScoped<LicenseValidator>();
builder.Services.AddScoped<FloatingLicenseService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Setup");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

DbInitializer.InitTables(app.Services.GetRequiredService<LicenseDb>());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<SetupRedirectMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapLicenseApi();
app.MapRazorPages();
app.Run();

public partial class Program;
