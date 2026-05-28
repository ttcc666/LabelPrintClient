using System.Text.Json.Serialization;

namespace LabelPrintClient.Modules.License.Models;

public sealed class LicenseDocument
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Id { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LicenseMode LicenseMode { get; set; } = LicenseMode.Standalone;

    public string MachineCode { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public DateTime ExpireTime { get; set; }

    public string IssuedTo { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessKey { get; set; }

    public string Signature { get; set; } = string.Empty;
}
