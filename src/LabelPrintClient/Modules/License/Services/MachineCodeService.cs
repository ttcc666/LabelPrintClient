using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace LabelPrintClient.Modules.License.Services;

public static class MachineCodeService
{
    public static string GetMachineCode()
    {
        var parts = new[]
        {
            ReadMachineGuid(),
            Environment.MachineName,
            Environment.ProcessorCount.ToString()
        };

        return GenerateFromParts(parts);
    }

    public static string GenerateFromParts(IEnumerable<string?> parts)
    {
        var normalized = string.Join("|", parts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim().ToUpperInvariant()));

        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "LABEL_PRINT_CLIENT";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).Insert(16, "-").Insert(33, "-")[..50];
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
