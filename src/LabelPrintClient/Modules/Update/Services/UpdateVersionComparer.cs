namespace LabelPrintClient.Modules.Update.Services;

public static class UpdateVersionComparer
{
    public static bool IsNewer(string candidateVersion, string currentVersion)
    {
        return Parse(candidateVersion) > Parse(currentVersion);
    }

    public static Version Parse(string? version)
    {
        var normalized = (version ?? string.Empty).Trim().TrimStart('v', 'V');
        var dashIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
            normalized = normalized[..dashIndex];

        return Version.TryParse(normalized, out var parsed)
            ? parsed
            : new Version(0, 0, 0);
    }
}
