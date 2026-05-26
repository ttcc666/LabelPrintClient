using LabelPrintClient.Infrastructure;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public static class SearchTextBuilder
{
    private static readonly char[] Separators = [' ', '\t', '\r', '\n'];

    public static string FromJson(string? rowDataJson)
    {
        if (string.IsNullOrWhiteSpace(rowDataJson))
            return string.Empty;

        try
        {
            var data = JsonHelper.Deserialize<Dictionary<string, string>>(rowDataJson);
            return FromDictionary(data);
        }
        catch
        {
            return Normalize(rowDataJson);
        }
    }

    public static string FromDictionary(IReadOnlyDictionary<string, string>? data)
    {
        if (data == null || data.Count == 0)
            return string.Empty;

        var parts = data
            .SelectMany(x => new[] { x.Key, x.Value })
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return Normalize(string.Join(' ', parts));
    }

    private static string Normalize(string text)
    {
        return string.Join(' ', text
            .Trim()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
