using LicenseServer.Infrastructure;
using LicenseServer.Models;
using Microsoft.Extensions.Caching.Memory;

namespace LicenseServer.Services;

public sealed class AdminAuthService
{
    private const string HasAnyAdminCacheKey = "license-server:has-any-admin";
    private static readonly TimeSpan HasAnyAdminCacheDuration = TimeSpan.FromSeconds(30);

    private readonly LicenseDb _db;
    private readonly IMemoryCache _cache;

    public AdminAuthService(LicenseDb db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> HasAnyAdminAsync()
    {
        if (_cache.TryGetValue(HasAnyAdminCacheKey, out bool hasAnyAdmin))
            return hasAnyAdmin;

        hasAnyAdmin = await _db.Db.Queryable<AdminUser>().AnyAsync();
        _cache.Set(HasAnyAdminCacheKey, hasAnyAdmin, HasAnyAdminCacheDuration);
        return hasAnyAdmin;
    }

    public async Task<AdminUser> CreateInitialAdminAsync(string password, string displayName = "Administrator")
    {
        if (await HasAnyAdminAsync())
            throw new InvalidOperationException("管理员账号已经初始化。");

        var hash = PasswordHasher.Hash(password);
        var user = new AdminUser
        {
            Id = IdHelper.NewId(),
            UserName = "System",
            DisplayName = displayName,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordIterations = hash.Iterations,
            IsEnabled = true,
            CreateTime = DateTime.Now
        };

        await _db.Db.Insertable(user).ExecuteCommandAsync();
        _cache.Set(HasAnyAdminCacheKey, true, HasAnyAdminCacheDuration);
        return user;
    }

    public async Task<AdminUser?> ValidateAsync(string userName, string password)
    {
        var users = await _db.Db.Queryable<AdminUser>()
            .Where(x => x.UserName == userName)
            .ToListAsync();
        var user = users.FirstOrDefault(x => string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
        if (user == null || !user.IsEnabled)
            return null;

        return PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations)
            ? user
            : null;
    }
}
