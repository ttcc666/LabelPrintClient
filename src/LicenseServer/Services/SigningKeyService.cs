using System.Security.Cryptography;
using LicenseServer.Infrastructure;
using LicenseServer.Models;

namespace LicenseServer.Services;

public sealed class SigningKeyService
{
    private readonly LicenseDb _db;
    private readonly PrivateKeyProtector _protector;

    public SigningKeyService(LicenseDb db, PrivateKeyProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<SigningKey?> GetActiveAsync()
    {
        return await _db.Db.Queryable<SigningKey>()
            .Where(x => x.IsEnabled)
            .OrderByDescending(x => x.CreateTime)
            .FirstAsync();
    }

    public async Task<SigningKey> ImportAsync(string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var publicKey = rsa.ExportRSAPublicKey();
        var key = new SigningKey
        {
            Id = IdHelper.NewId(),
            EncryptedPrivateKeyPem = _protector.Protect(privateKeyPem),
            PublicKeyFingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant(),
            IsEnabled = true,
            CreateTime = DateTime.Now
        };

        await _db.Db.Updateable<SigningKey>()
            .SetColumns(x => x.IsEnabled == false)
            .Where(x => x.IsEnabled)
            .ExecuteCommandAsync();
        await _db.Db.Insertable(key).ExecuteCommandAsync();
        return key;
    }

    public async Task<RSA> OpenActivePrivateKeyAsync()
    {
        var key = await GetActiveAsync();
        if (key == null)
            throw new InvalidOperationException("尚未导入签名私钥。");

        var pem = _protector.Unprotect(key.EncryptedPrivateKeyPem);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
