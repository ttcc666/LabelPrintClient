using LicenseServer.Api;
using LicenseServer.Config;
using LicenseServer.Infrastructure;
using LicenseServer.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

var licenseServerOptions = builder.Configuration.GetSection("LicenseServer").Get<LicenseServerOptions>() ?? new LicenseServerOptions();
var maxUpdateUploadBytes = licenseServerOptions.MaxUpdateUploadBytes;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUpdateUploadBytes;
});

builder.Services.Configure<LicenseServerOptions>(builder.Configuration.GetSection("LicenseServer"));
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = maxUpdateUploadBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUpdateUploadBytes;
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LicenseDb>();
builder.Services.AddScoped<AdminAuthService>();
builder.Services.AddScoped<PrivateKeyProtector>();
builder.Services.AddScoped<SigningKeyService>();
builder.Services.AddScoped<StandaloneLicenseGenerator>();
builder.Services.AddScoped<LicenseValidator>();
builder.Services.AddScoped<FloatingLicenseService>();
builder.Services.AddScoped<UpdateReleaseService>();
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
app.MapUpdateApi();
app.MapRazorPages();
app.Run();

public partial class Program;
