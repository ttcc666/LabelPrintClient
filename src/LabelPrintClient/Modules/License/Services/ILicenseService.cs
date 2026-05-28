using LabelPrintClient.Modules.License.Models;

namespace LabelPrintClient.Modules.License.Services;

public interface ILicenseService
{
    Task<LicenseResult> ValidateStartupAsync(CancellationToken cancellationToken = default);

    Task<LicenseResult> AcquireAsync(CancellationToken cancellationToken = default);

    Task<LicenseResult> HeartbeatAsync(CancellationToken cancellationToken = default);

    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
