using System.Security.Cryptography;

namespace LabelPrintClient.Modules.Auth.Services;

public sealed record PasswordHashResult(string Hash, string Salt, int Iterations);

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int DefaultIterations = 120_000;

    public static PasswordHashResult Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("密码不能为空。", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return new PasswordHashResult(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            DefaultIterations);
    }

    public static bool Verify(string password, string hash, string salt, int iterations)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(hash) ||
            string.IsNullOrEmpty(salt) ||
            iterations <= 0)
        {
            return false;
        }

        var saltBytes = Convert.FromBase64String(salt);
        var expected = Convert.FromBase64String(hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
