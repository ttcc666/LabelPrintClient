using System.Text.Json.Serialization;
using LicenseServer.Models;

namespace LicenseServer.Services;

public sealed class LicenseDocument
{
    public string ProductCode { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LicenseMode LicenseMode { get; set; } = LicenseMode.Standalone;

    public string MachineCode { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public DateTime ExpireTime { get; set; }

    public string IssuedTo { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;
}
