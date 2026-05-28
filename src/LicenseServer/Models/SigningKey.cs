using SqlSugar;

namespace LicenseServer.Models;

[SugarTable("license_signing_key")]
public sealed class SigningKey
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(ColumnDataType = "TEXT")]
    public string EncryptedPrivateKeyPem { get; set; } = string.Empty;

    [SugarColumn(Length = 128)]
    public string PublicKeyFingerprint { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreateTime { get; set; } = DateTime.Now;
}
