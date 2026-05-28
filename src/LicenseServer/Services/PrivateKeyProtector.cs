using System.Security.Cryptography;
using System.Text;
using LicenseServer.Config;
using Microsoft.Extensions.Options;

namespace LicenseServer.Services;

public sealed class PrivateKeyProtector
{
    private readonly LicenseServerOptions _options;

    public PrivateKeyProtector(IOptions<LicenseServerOptions> options)
    {
        _options = options.Value;
    }

    public bool HasMasterKey => !string.IsNullOrWhiteSpace(ReadMasterKey());

    public string Protect(string privateKeyPem)
    {
        var masterKey = ReadMasterKey();
        if (string.IsNullOrWhiteSpace(masterKey))
            throw new InvalidOperationException($"缺少 MasterKey 配置或环境变量 {_options.MasterKeyEnvironmentName}，不能保存签名私钥。");

        var key = SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(privateKeyPem);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(ciphertext)}";
    }

    public string Unprotect(string protectedText)
    {
        var masterKey = ReadMasterKey();
        if (string.IsNullOrWhiteSpace(masterKey))
            throw new InvalidOperationException($"缺少 MasterKey 配置或环境变量 {_options.MasterKeyEnvironmentName}，不能读取签名私钥。");

        var parts = protectedText.Split('.');
        if (parts.Length != 3)
            throw new InvalidOperationException("签名私钥密文格式无效。");

        var key = SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var ciphertext = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private string? ReadMasterKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.MasterKey))
            return _options.MasterKey;

        return Environment.GetEnvironmentVariable(_options.MasterKeyEnvironmentName);
    }
}
