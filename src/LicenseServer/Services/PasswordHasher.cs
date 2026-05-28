using System.Security.Cryptography;

namespace LicenseServer.Services;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static PasswordHashResult Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return new PasswordHashResult(Convert.ToBase64String(hash), Convert.ToBase64String(salt), Iterations);
    }

    public static bool Verify(string password, string expectedHash, string salt, int iterations)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var expectedBytes = Convert.FromBase64String(expectedHash);
        var actualBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, expectedBytes.Length);
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}

public sealed record PasswordHashResult(string Hash, string Salt, int Iterations);
