using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public async Task<string> GenerateAsync(AppLicense license, string? accessKey = null)
    {
        if (license.LicenseMode == LicenseMode.Standalone && string.IsNullOrWhiteSpace(license.MachineCode))
            throw new InvalidOperationException("单机授权必须填写机器码。");

        var document = new LicenseDocument
        {
            Id = license.Id,
            ProductCode = license.ProductCode,
            LicenseMode = license.LicenseMode,
            MachineCode = license.MachineCode ?? string.Empty,
            TotalCount = license.LicenseMode == LicenseMode.Standalone ? 1 : license.TotalCount,
            ExpireTime = license.ExpireTime,
            IssuedTo = license.IssuedTo,
            AccessKey = license.LicenseMode == LicenseMode.Floating ? accessKey : null
        };

        using var rsa = await _signingKeys.OpenActivePrivateKeyAsync();
        var payload = Encoding.UTF8.GetBytes(CreateSignedPayload(document));
        document.Signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static string CreateSignedPayload(LicenseDocument document)
    {
        var payload = new LicensePayload(
            document.Id,
            document.ProductCode,
            document.LicenseMode,
            document.MachineCode,
            document.TotalCount,
            document.ExpireTime,
            document.IssuedTo,
            document.AccessKey);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Serialize(payload, options);
    }

    private sealed record LicensePayload(
        long? Id,
        string ProductCode,
        LicenseMode LicenseMode,
        string MachineCode,
        int TotalCount,
        DateTime ExpireTime,
        string IssuedTo,
        string? AccessKey = null);
}
