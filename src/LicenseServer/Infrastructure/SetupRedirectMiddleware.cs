using LicenseServer.Services;

namespace LicenseServer.Infrastructure;

public sealed class SetupRedirectMiddleware
{
    private readonly RequestDelegate _next;

    public SetupRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AdminAuthService auth)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isAllowedWithoutAdmin =
            path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/license", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedWithoutAdmin && !await auth.HasAnyAdminAsync())
        {
            context.Response.Redirect("/setup");
            return;
        }

        await _next(context);
    }
}
