using System.Security.Cryptography;
using System.Text;

namespace LicenseServer.Services;

public static class HashService
{
    public static string Sha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public static string NewSecret(int bytes = 32)
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
    }
}
