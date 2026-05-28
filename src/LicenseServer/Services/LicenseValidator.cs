using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseServer.Config;
using Microsoft.Extensions.Options;

namespace LicenseServer.Services;

public sealed class LicenseValidator
{
    private const string DefaultPublicKeyPem = """
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

    private readonly LicenseServerOptions _options;

    public LicenseValidator(IOptions<LicenseServerOptions> options)
    {
        _options = options.Value;
    }

    public bool Verify(LicenseDocument document)
    {
        if (document == null || string.IsNullOrWhiteSpace(document.Signature))
            return false;

        try
        {
            var publicKey = string.IsNullOrWhiteSpace(_options.PublicKeyPem)
                ? DefaultPublicKeyPem
                : _options.PublicKeyPem;

            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);

            var payload = Encoding.UTF8.GetBytes(StandaloneLicenseGenerator.CreateSignedPayload(document));
            var signature = Convert.FromBase64String(document.Signature);
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
}
