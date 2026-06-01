using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;

namespace LabelPrintClient.Modules.License.Services;

public sealed class LicenseService : ILicenseService
{
    private readonly AppSettings _settings;
    private readonly StandaloneLicenseVerifier _standaloneVerifier;
    private readonly FloatingLicenseClient _floatingClient;
    private string? _token;

    public LicenseService(
        AppSettings settings,
        StandaloneLicenseVerifier? standaloneVerifier = null,
        FloatingLicenseClient? floatingClient = null)
    {
        _settings = settings;
        _standaloneVerifier = standaloneVerifier ?? new StandaloneLicenseVerifier();
        _floatingClient = floatingClient ?? new FloatingLicenseClient();
    }

    public Task<LicenseResult> ValidateStartupAsync(CancellationToken cancellationToken = default)
    {
        return _settings.LicenseMode == LicenseMode.Standalone
            ? _standaloneVerifier.VerifyAsync(_settings, cancellationToken)
            : AcquireAsync(cancellationToken);
    }

    public Task<LicenseResult> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return _settings.LicenseMode == LicenseMode.Standalone
            ? _standaloneVerifier.VerifyAsync(_settings, cancellationToken)
            : _floatingClient.ValidateAsync(_settings, cancellationToken);
    }

    public async Task<LicenseResult> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.LicenseMode == LicenseMode.Standalone)
            return await _standaloneVerifier.VerifyAsync(_settings, cancellationToken).ConfigureAwait(false);

        var result = await _floatingClient.AcquireAsync(_settings, cancellationToken).ConfigureAwait(false);
        if (result.IsValid)
            _token = result.Token;

        return result;
    }

    public Task<LicenseResult> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        return _settings.LicenseMode == LicenseMode.Standalone
            ? _standaloneVerifier.VerifyAsync(_settings, cancellationToken)
            : _floatingClient.HeartbeatAsync(_token ?? string.Empty, _settings, cancellationToken);
    }

    public Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        return _settings.LicenseMode == LicenseMode.Floating
            ? _floatingClient.ReleaseAsync(_token, _settings, cancellationToken)
            : Task.CompletedTask;
    }
}
