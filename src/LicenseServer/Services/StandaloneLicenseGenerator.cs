using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseServer.Models;

namespace LicenseServer.Services;

public sealed class StandaloneLicenseGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SigningKeyService _signingKeys;

    public StandaloneLicenseGenerator(SigningKeyService signingKeys)
    {
        _signingKeys = signingKeys;
    }

    public async Task<string> GenerateAsync(AppLicense license)
    {
        if (license.LicenseMode != LicenseMode.Standalone)
            throw new InvalidOperationException("只有单机授权可以生成授权文件。");
        if (string.IsNullOrWhiteSpace(license.MachineCode))
            throw new InvalidOperationException("单机授权必须填写机器码。");

        var document = new LicenseDocument
        {
            ProductCode = license.ProductCode,
            LicenseMode = LicenseMode.Standalone,
            MachineCode = license.MachineCode,
            TotalCount = 1,
            ExpireTime = license.ExpireTime,
            IssuedTo = license.IssuedTo
        };

        using var rsa = await _signingKeys.OpenActivePrivateKeyAsync();
        var payload = Encoding.UTF8.GetBytes(CreateSignedPayload(document));
        document.Signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static string CreateSignedPayload(LicenseDocument document)
    {
        var payload = new LicensePayload(
            document.ProductCode,
            document.LicenseMode,
            document.MachineCode,
            document.TotalCount,
            document.ExpireTime,
            document.IssuedTo);

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false });
    }

    private sealed record LicensePayload(
        string ProductCode,
        LicenseMode LicenseMode,
        string MachineCode,
        int TotalCount,
        DateTime ExpireTime,
        string IssuedTo);
}
