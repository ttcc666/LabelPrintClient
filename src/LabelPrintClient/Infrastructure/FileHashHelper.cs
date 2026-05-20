using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LabelPrintClient.Infrastructure;

public static class FileHashHelper
{
    public static string GetSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(stream));
    }

    public static string GetSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(bytes));
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
