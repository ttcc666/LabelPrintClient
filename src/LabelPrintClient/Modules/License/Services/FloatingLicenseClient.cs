using System.Net.Http;
using System.Net.Http.Json;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;

namespace LabelPrintClient.Modules.License.Services;

public sealed class FloatingLicenseClient
{
    private readonly HttpClient _http;

    public FloatingLicenseClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public async Task<LicenseResult> AcquireAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.LicenseServerUrl))
            return LicenseResult.Failure(LicenseStatus.InvalidConfiguration, LicenseMode.Floating, "未配置浮动授权服务器地址。");

        try
        {
            ConfigureBaseAddress(settings.LicenseServerUrl);
            var response = await _http.PostAsJsonAsync("api/license/acquire", new LicenseAcquireRequest(
                settings.ProductCode,
                settings.LicenseAccessKey,
                MachineCodeService.GetMachineCode(),
                Environment.MachineName), cancellationToken).ConfigureAwait(false);

            return await ReadResultAsync(response, LicenseMode.Floating, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return LicenseResult.Failure(LicenseStatus.ServerUnavailable, LicenseMode.Floating, "授权服务器不可用。");
        }
    }

    public async Task<LicenseResult> HeartbeatAsync(string token, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return LicenseResult.Failure(LicenseStatus.Rejected, LicenseMode.Floating, "浮动授权令牌为空。");

        try
        {
            ConfigureBaseAddress(settings.LicenseServerUrl);
            var response = await _http.PostAsJsonAsync("api/license/heartbeat", new LicenseHeartbeatRequest(token), cancellationToken).ConfigureAwait(false);
            return await ReadResultAsync(response, LicenseMode.Floating, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return LicenseResult.Failure(LicenseStatus.ServerUnavailable, LicenseMode.Floating, "授权服务器心跳失败。");
        }
    }

    public async Task ReleaseAsync(string? token, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(settings.LicenseServerUrl))
            return;

        try
        {
            ConfigureBaseAddress(settings.LicenseServerUrl);
            await _http.PostAsJsonAsync("api/license/release", new LicenseHeartbeatRequest(token), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void ConfigureBaseAddress(string serverUrl)
    {
        var baseAddress = serverUrl.EndsWith("/", StringComparison.Ordinal) ? serverUrl : serverUrl + "/";
        var uri = new Uri(baseAddress, UriKind.Absolute);
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = uri;
            return;
        }

        if (!_http.BaseAddress.Equals(uri))
            throw new InvalidOperationException("授权服务器地址不能在同一个 HTTP 客户端实例中切换。");
    }

    private static async Task<LicenseResult> ReadResultAsync(HttpResponseMessage response, LicenseMode mode, CancellationToken cancellationToken)
    {
        LicenseServerResponse? body = null;
        try
        {
            body = await response.Content.ReadFromJsonAsync<LicenseServerResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }

        if (response.IsSuccessStatusCode && body?.Success == true)
        {
            return LicenseResult.Success(
                mode,
                string.IsNullOrWhiteSpace(body.Message) ? "浮动授权有效。" : body.Message,
                body.ExpireTime,
                body.IssuedTo,
                body.Token,
                body.HeartbeatIntervalSeconds > 0 ? body.HeartbeatIntervalSeconds : 30);
        }

        var status = response.StatusCode == System.Net.HttpStatusCode.Conflict
            ? LicenseStatus.SeatLimitExceeded
            : LicenseStatus.Rejected;

        return LicenseResult.Failure(status, mode, body?.Message ?? "授权服务器拒绝请求。");
    }

    private sealed record LicenseAcquireRequest(string ProductCode, string AccessKey, string MachineCode, string MachineName);

    private sealed record LicenseHeartbeatRequest(string Token);

    private sealed class LicenseServerResponse
    {
        public bool Success { get; set; }

        public string? Token { get; set; }

        public string? Message { get; set; }

        public string? IssuedTo { get; set; }

        public DateTime? ExpireTime { get; set; }

        public int HeartbeatIntervalSeconds { get; set; }
    }
}
