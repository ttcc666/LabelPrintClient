using LicenseServer.Config;
using LicenseServer.Infrastructure;
using LicenseServer.Models;
using Microsoft.Extensions.Options;

namespace LicenseServer.Services;

public sealed class FloatingLicenseService
{
    private readonly LicenseDb _db;
    private readonly LicenseServerOptions _options;

    public FloatingLicenseService(LicenseDb db, IOptions<LicenseServerOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<FloatingLicenseResponse> AcquireAsync(LicenseAcquireRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProductCode) ||
            string.IsNullOrWhiteSpace(request.AccessKey) ||
            string.IsNullOrWhiteSpace(request.MachineCode))
        {
            return FloatingLicenseResponse.Fail(400, "ProductCode、AccessKey 和 MachineCode 不能为空。");
        }

        var accessKeyHash = HashService.Sha256(request.AccessKey);
        var license = await _db.Db.Queryable<AppLicense>()
            .FirstAsync(x => x.AccessKeyHash == accessKeyHash);

        var validation = ValidateLicense(license, request.ProductCode);
        if (validation != null)
            return validation;

        await CleanupExpiredSessionsAsync(license!.Id);

        var now = DateTime.Now;
        var timeout = TimeSpan.FromSeconds(Math.Max(30, _options.SessionTimeoutSeconds));
        var existing = await _db.Db.Queryable<OnlineSession>()
            .FirstAsync(x => x.LicenseId == license.Id &&
                             x.MachineCode == request.MachineCode &&
                             x.ExpireTime > now);
        if (existing != null)
        {
            var renewedToken = HashService.NewSecret();
            existing.LastHeartbeat = now;
            existing.ExpireTime = now.Add(timeout);
            existing.TokenHash = HashService.Sha256(renewedToken);
            existing.MachineName = request.MachineName;
            await _db.Db.Updateable(existing)
                .UpdateColumns(x => new { x.LastHeartbeat, x.ExpireTime, x.TokenHash, x.MachineName })
                .ExecuteCommandAsync();

            return FloatingLicenseResponse.Ok(renewedToken, license, _options.HeartbeatIntervalSeconds);
        }

        var activeCount = await _db.Db.Queryable<OnlineSession>()
            .CountAsync(x => x.LicenseId == license.Id && x.ExpireTime > now);
        if (activeCount >= license.TotalCount)
            return FloatingLicenseResponse.Fail(409, "浮动许可席位已满。");

        var token = HashService.NewSecret();
        var session = new OnlineSession
        {
            Id = IdHelper.NewId(),
            LicenseId = license.Id,
            MachineCode = request.MachineCode,
            MachineName = request.MachineName,
            TokenHash = HashService.Sha256(token),
            LoginTime = now,
            LastHeartbeat = now,
            ExpireTime = now.Add(timeout)
        };
        await _db.Db.Insertable(session).ExecuteCommandAsync();

        return FloatingLicenseResponse.Ok(token, license, _options.HeartbeatIntervalSeconds);
    }

    public async Task<FloatingLicenseResponse> ValidateAsync(LicenseAcquireRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProductCode) ||
            string.IsNullOrWhiteSpace(request.AccessKey) ||
            string.IsNullOrWhiteSpace(request.MachineCode))
        {
            return FloatingLicenseResponse.Fail(400, "ProductCode、AccessKey 和 MachineCode 不能为空。");
        }

        var accessKeyHash = HashService.Sha256(request.AccessKey);
        var license = await _db.Db.Queryable<AppLicense>()
            .FirstAsync(x => x.AccessKeyHash == accessKeyHash);

        var validation = ValidateLicense(license, request.ProductCode);
        return validation ?? FloatingLicenseResponse.OkValidated(license!, _options.HeartbeatIntervalSeconds);
    }

    public async Task<FloatingLicenseResponse> HeartbeatAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return FloatingLicenseResponse.Fail(400, "Token 不能为空。");

        var now = DateTime.Now;
        var session = await _db.Db.Queryable<OnlineSession>()
            .FirstAsync(x => x.TokenHash == HashService.Sha256(token));
        if (session == null || session.ExpireTime <= now)
            return FloatingLicenseResponse.Fail(404, "浮动授权会话不存在或已过期。");

        var license = await _db.Db.Queryable<AppLicense>().FirstAsync(x => x.Id == session.LicenseId);
        var validation = ValidateLicense(license, license?.ProductCode ?? string.Empty);
        if (validation != null)
            return validation;

        session.LastHeartbeat = now;
        session.ExpireTime = now.AddSeconds(Math.Max(30, _options.SessionTimeoutSeconds));
        await _db.Db.Updateable(session)
            .UpdateColumns(x => new { x.LastHeartbeat, x.ExpireTime })
            .ExecuteCommandAsync();

        return FloatingLicenseResponse.Ok(token, license!, _options.HeartbeatIntervalSeconds);
    }

    public async Task ReleaseAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        await _db.Db.Deleteable<OnlineSession>()
            .Where(x => x.TokenHash == HashService.Sha256(token))
            .ExecuteCommandAsync();
    }

    public async Task ForceReleaseAsync(long sessionId)
    {
        await _db.Db.Deleteable<OnlineSession>()
            .Where(x => x.Id == sessionId)
            .ExecuteCommandAsync();
    }

    private async Task CleanupExpiredSessionsAsync(long licenseId)
    {
        await _db.Db.Deleteable<OnlineSession>()
            .Where(x => x.LicenseId == licenseId && x.ExpireTime <= DateTime.Now)
            .ExecuteCommandAsync();
    }

    private static FloatingLicenseResponse? ValidateLicense(AppLicense? license, string productCode)
    {
        if (license == null)
            return FloatingLicenseResponse.Fail(401, "浮动许可证不存在或访问密钥错误。");
        if (!license.IsEnabled)
            return FloatingLicenseResponse.Fail(401, "浮动许可证已禁用。");
        if (license.LicenseMode != LicenseMode.Floating)
            return FloatingLicenseResponse.Fail(401, "该许可证不是浮动授权。");
        if (!string.Equals(license.ProductCode, productCode, StringComparison.OrdinalIgnoreCase))
            return FloatingLicenseResponse.Fail(401, "产品编码不匹配。");
        if (license.ExpireTime <= DateTime.Now)
            return FloatingLicenseResponse.Fail(401, "浮动许可证已过期。");

        return null;
    }
}

public sealed record LicenseAcquireRequest(string ProductCode, string AccessKey, string MachineCode, string? MachineName);

public sealed record LicenseHeartbeatRequest(string Token);

public sealed class FloatingLicenseResponse
{
    public bool Success { get; init; }

    public string? Token { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? IssuedTo { get; init; }

    public DateTime? ExpireTime { get; init; }

    public int HeartbeatIntervalSeconds { get; init; }

    public int StatusCode { get; init; } = 200;

    public static FloatingLicenseResponse Ok(string token, AppLicense license, int heartbeatIntervalSeconds)
    {
        return new FloatingLicenseResponse
        {
            Success = true,
            Token = token,
            Message = "浮动授权有效。",
            IssuedTo = license.IssuedTo,
            ExpireTime = license.ExpireTime,
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds > 0 ? heartbeatIntervalSeconds : 30,
            StatusCode = 200
        };
    }

    public static FloatingLicenseResponse OkValidated(AppLicense license, int heartbeatIntervalSeconds)
    {
        return new FloatingLicenseResponse
        {
            Success = true,
            Token = null,
            Message = "浮动授权有效。",
            IssuedTo = license.IssuedTo,
            ExpireTime = license.ExpireTime,
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds > 0 ? heartbeatIntervalSeconds : 30,
            StatusCode = 200
        };
    }

    public static FloatingLicenseResponse Fail(int statusCode, string message)
    {
        return new FloatingLicenseResponse
        {
            Success = false,
            Message = message,
            StatusCode = statusCode
        };
    }
}
