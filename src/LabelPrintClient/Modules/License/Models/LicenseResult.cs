namespace LabelPrintClient.Modules.License.Models;

public sealed class LicenseResult
{
    public static LicenseResult Success(
        LicenseMode mode,
        string message,
        DateTime? expireTime = null,
        string? issuedTo = null,
        string? token = null,
        int heartbeatIntervalSeconds = 30)
    {
        return new LicenseResult
        {
            IsValid = true,
            Status = LicenseStatus.Valid,
            Mode = mode,
            Message = message,
            ExpireTime = expireTime,
            IssuedTo = issuedTo,
            Token = token,
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds
        };
    }

    public static LicenseResult Failure(LicenseStatus status, LicenseMode mode, string message)
    {
        return new LicenseResult
        {
            IsValid = false,
            Status = status,
            Mode = mode,
            Message = message
        };
    }

    public bool IsValid { get; init; }

    public LicenseStatus Status { get; init; }

    public LicenseMode Mode { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTime? ExpireTime { get; init; }

    public string? IssuedTo { get; init; }

    public string? Token { get; init; }

    public int HeartbeatIntervalSeconds { get; init; } = 30;
}
