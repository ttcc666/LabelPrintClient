namespace LabelPrintClient.Modules.License.Models;

public enum LicenseStatus
{
    Valid = 0,
    Missing = 1,
    Expired = 2,
    MachineMismatch = 3,
    ServerUnavailable = 4,
    SeatLimitExceeded = 5,
    InvalidSignature = 6,
    InvalidConfiguration = 7,
    Rejected = 8
}
