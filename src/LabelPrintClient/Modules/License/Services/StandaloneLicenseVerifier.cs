using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;

namespace LabelPrintClient.Modules.License.Services;

public sealed class StandaloneLicenseVerifier
{
    internal const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAiHFeNRm3nGEe95+M6hQ4
tHyi58RxQDWS3Na1tt7v8zOCUggs8scjM8f3XLkWhDXp5NH3fUZxchUKuLPMlXUh
dPxsAJ7oWN9v0qSPO7sNUqjudaxqx8HQO2ATC4tHHKPdxKbIcSJXXPldQXocO+VQ
QRNVaPFSnm5WBtbiOUjShCd3xHZikST9oaFiUKskV56KN4ve8eEnAq+LIA9+EtkH
si6+KuklhFVN+FNN/ikt8TSf3yx0Y1tLS113Ncg3chYLRdf4wXfhRIyX7GbTNFGS
b3PIf/ynQYG48TyOJDbmO9+DxU7qsMr8eE5TOsjHRGsWXIgJ8dVixF2e3ywAhzKJ
xwIDAQAB
-----END PUBLIC KEY-----
""";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<LicenseResult> VerifyAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.StandaloneLicenseFilePath))
            return LicenseResult.Failure(LicenseStatus.Missing, LicenseMode.Standalone, "未配置单机授权文件。");

        var path = ResolveLicensePath(settings.StandaloneLicenseFilePath);
        if (!File.Exists(path))
            return LicenseResult.Failure(LicenseStatus.Missing, LicenseMode.Standalone, "单机授权文件不存在。");

        LicenseDocument? document;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            document = JsonSerializer.Deserialize<LicenseDocument>(json, JsonOptions);
        }
        catch
        {
            return LicenseResult.Failure(LicenseStatus.InvalidSignature, LicenseMode.Standalone, "授权文件格式无效。");
        }

        if (document == null || string.IsNullOrWhiteSpace(document.Signature))
            return LicenseResult.Failure(LicenseStatus.InvalidSignature, LicenseMode.Standalone, "授权文件缺少签名。");

        if (!string.Equals(document.ProductCode, settings.ProductCode, StringComparison.OrdinalIgnoreCase))
            return LicenseResult.Failure(LicenseStatus.InvalidConfiguration, LicenseMode.Standalone, "授权产品编码不匹配。");

        if (document.LicenseMode != LicenseMode.Standalone)
            return LicenseResult.Failure(LicenseStatus.InvalidConfiguration, LicenseMode.Standalone, "授权文件不是单机授权。");

        if (!VerifySignature(document))
            return LicenseResult.Failure(LicenseStatus.InvalidSignature, LicenseMode.Standalone, "授权文件签名无效。");

        if (document.ExpireTime <= DateTime.Now)
            return LicenseResult.Failure(LicenseStatus.Expired, LicenseMode.Standalone, "授权已过期。");

        var machineCode = MachineCodeService.GetMachineCode();
        if (!string.Equals(document.MachineCode, machineCode, StringComparison.OrdinalIgnoreCase))
            return LicenseResult.Failure(LicenseStatus.MachineMismatch, LicenseMode.Standalone, "授权文件不属于当前机器。");

        return LicenseResult.Success(
            LicenseMode.Standalone,
            "单机授权有效。",
            document.ExpireTime,
            document.IssuedTo,
            heartbeatIntervalSeconds: settings.LicenseHeartbeatIntervalSeconds);
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

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static bool VerifySignature(LicenseDocument document)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            var payload = Encoding.UTF8.GetBytes(CreateSignedPayload(document));
            var signature = Convert.FromBase64String(document.Signature);
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveLicensePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    private sealed record LicensePayload(
        string ProductCode,
        LicenseMode LicenseMode,
        string MachineCode,
        int TotalCount,
        DateTime ExpireTime,
        string IssuedTo);
}
