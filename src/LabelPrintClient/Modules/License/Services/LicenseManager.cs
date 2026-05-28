using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;

namespace LabelPrintClient.Modules.License.Services;

public static class LicenseManager
{
    private static readonly object SyncRoot = new();
    private static ILicenseService _service = new LicenseService(new AppSettings());
    private static CancellationTokenSource? _heartbeatCts;
    private static LicenseResult? _current;
    private static DateTime _lastValidHeartbeat = DateTime.Now;

    public static LicenseResult? Current => _current;

    public static string MachineCode => MachineCodeService.GetMachineCode();

    public static void Initialize(AppSettings settings)
    {
        lock (SyncRoot)
        {
            _service = new LicenseService(settings);
            _current = null;
            _lastValidHeartbeat = DateTime.Now;
        }
    }

    public static async Task<LicenseResult> ValidateStartupAsync(CancellationToken cancellationToken = default)
    {
        var result = await _service.ValidateStartupAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsValid)
            _lastValidHeartbeat = DateTime.Now;
        _current = result;
        return result;
    }

    public static void StartHeartbeat(AppSettings settings, Action<LicenseResult> onExpired)
    {
        StopHeartbeat();
        _heartbeatCts = new CancellationTokenSource();
        _ = RunHeartbeatAsync(settings, onExpired, _heartbeatCts.Token);
    }

    public static void StopHeartbeat()
    {
        try
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _heartbeatCts = null;
        }
    }

    public static Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        StopHeartbeat();
        return _service.ReleaseAsync(cancellationToken);
    }

    private static async Task RunHeartbeatAsync(AppSettings settings, Action<LicenseResult> onExpired, CancellationToken cancellationToken)
    {
        var intervalSeconds = Math.Max(5, settings.LicenseHeartbeatIntervalSeconds);
        var timeoutSeconds = Math.Max(intervalSeconds, settings.LicenseHeartbeatTimeoutSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                    return;

                var result = await _service.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
                _current = result;
                if (result.IsValid)
                {
                    _lastValidHeartbeat = DateTime.Now;
                    continue;
                }

                if (DateTime.Now - _lastValidHeartbeat >= TimeSpan.FromSeconds(timeoutSeconds) ||
                    result.Status is LicenseStatus.Expired or LicenseStatus.Rejected or LicenseStatus.SeatLimitExceeded)
                {
                    onExpired(result);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                var result = LicenseResult.Failure(LicenseStatus.ServerUnavailable, settings.LicenseMode, "授权心跳异常。");
                _current = result;
                if (DateTime.Now - _lastValidHeartbeat >= TimeSpan.FromSeconds(timeoutSeconds))
                {
                    onExpired(result);
                    return;
                }
            }
        }
    }
}
