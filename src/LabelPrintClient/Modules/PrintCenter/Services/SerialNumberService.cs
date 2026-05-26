using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public static partial class SerialNumberService
{
    public const string DefaultPattern = "SN-{seq:0000}";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CounterLocks = new();

    public static bool IsValidPattern(
        string? pattern, 
        IEnumerable<string>? allowedFields, 
        out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "序列号规则不能为空。";
            return false;
        }

        var allowedSet = allowedFields != null
            ? new HashSet<string>(allowedFields, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hasSeq = false;
        foreach (Match match in TokenRegex().Matches(pattern))
        {
            var token = match.Groups[1].Value;
            if (token == "seq" || token.StartsWith("seq:", StringComparison.Ordinal))
            {
                hasSeq = true;
                if (token.StartsWith("seq:", StringComparison.Ordinal) &&
                    (token.Length == 4 || token[4..].Any(x => x != '0')))
                {
                    error = "流水号格式只支持 0 占位，例如 {seq:0000}。";
                    return false;
                }
                continue;
            }

            if (token is "yyyy" or "yy" or "MM" or "dd" or "HH" or "mm")
                continue;

            // 增强的字段存在性校验：只有当前模板已存在的字段 FieldCode 才允许作为序列号规则变量
            if (allowedSet.Contains(token))
                continue;

            error = $"不支持的序列号变量：{{{token}}}（当前模板变量字段中不存在此字段，请先添加它）。";
            return false;
        }

        if (!hasSeq)
        {
            error = "序列号规则必须包含 {seq} 或 {seq:0000}。";
            return false;
        }

        return true;
    }

    public static string Preview(
        string? pattern, 
        DateTime now, 
        long sequence = 1,
        IReadOnlyDictionary<string, string>? rowData = null)
    {
        return Format(NormalizePattern(pattern, null), now, sequence, rowData);
    }

    public static async Task<string> GenerateNextAsync(
        LabelTemplate template,
        LabelImportRow row,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var counterKey = BuildCounterKey(template.SerialResetPeriod, now);
        var counterLock = GetCounterLock(template.Id, row.Id, counterKey);
        await counterLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var nextValue = await NextCounterValueAsync(template.Id, row.Id, counterKey, now, cancellationToken)
                .ConfigureAwait(false);
            
            // 解析行数据的自定义字典以供格式化替换
            var rowDict = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson);

            return Format(NormalizePattern(template.SerialNumberPattern, template.SerialNumberPrefix), now, nextValue, rowDict);
        }
        finally
        {
            counterLock.Release();
        }
    }

    public static async Task<long> GetCurrentValueAsync(
        LabelTemplate template,
        LabelImportRow row,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var counterKey = BuildCounterKey(template.SerialResetPeriod, now);
        var counter = await LoadCounterAsync(template.Id, row.Id, counterKey, cancellationToken).ConfigureAwait(false);
        return counter?.CurrentValue ?? 0;
    }

    public static string NormalizePattern(string? pattern, string? legacyPrefix)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
            return pattern.Trim();

        var prefix = string.IsNullOrWhiteSpace(legacyPrefix) ? "SN-" : legacyPrefix.Trim();
        return $"{prefix}{{seq:0000}}";
    }

    private static string BuildCounterKey(SerialResetPeriod period, DateTime now)
    {
        return period switch
        {
            SerialResetPeriod.Daily => now.ToString("yyyyMMdd"),
            SerialResetPeriod.Monthly => now.ToString("yyyyMM"),
            SerialResetPeriod.Yearly => now.ToString("yyyy"),
            _ => "global"
        };
    }

    private static SemaphoreSlim GetCounterLock(long templateId, long importRowId, string counterKey)
    {
        var lockKey = $"{templateId}:{importRowId}:{counterKey}";
        return CounterLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
    }

    private static async Task<long> NextCounterValueAsync(
        long templateId,
        long importRowId,
        string counterKey,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (AppDb.CurrentDbType == SqlSugar.DbType.PostgreSQL)
            return await NextPostgreSqlCounterValueAsync(templateId, importRowId, counterKey, now, cancellationToken)
                .ConfigureAwait(false);

        return await CreateOrAdvanceCounterAsync(templateId, importRowId, counterKey, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<long> NextPostgreSqlCounterValueAsync(
        long templateId,
        long importRowId,
        string counterKey,
        DateTime now,
        CancellationToken cancellationToken)
    {
        long nextValue = 0;
        await AppDb.UseTranAsync(async () =>
        {
            await AppDb.Db.Ado.GetScalarAsync(
                "SELECT pg_advisory_xact_lock(hashtext(@ScopeKey))",
                new { ScopeKey = $"{templateId}:{importRowId}:{counterKey}" },
                cancellationToken).ConfigureAwait(false);

            nextValue = await CreateOrAdvanceCounterAsync(templateId, importRowId, counterKey, now, cancellationToken)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
        return nextValue;
    }

    private static async Task<long> CreateOrAdvanceCounterAsync(
        long templateId,
        long importRowId,
        string counterKey,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var counter = await LoadCounterAsync(templateId, importRowId, counterKey, cancellationToken)
            .ConfigureAwait(false);
        if (counter == null)
        {
            counter = new LabelSerialCounter
            {
                Id = IdHelper.NewId(),
                TemplateId = templateId,
                ImportRowId = importRowId,
                CounterKey = counterKey,
                CurrentValue = 1,
                UpdateTime = now
            };
            await AppDb.Db.Insertable(counter).ExecuteCommandAsync().ConfigureAwait(false);
            return counter.CurrentValue;
        }

        counter.CurrentValue++;
        counter.UpdateTime = now;
        await AppDb.Db.Updateable(counter).ExecuteCommandAsync().ConfigureAwait(false);
        return counter.CurrentValue;
    }

    private static async Task<LabelSerialCounter?> LoadCounterAsync(
        long templateId,
        long importRowId,
        string counterKey,
        CancellationToken cancellationToken)
    {
        var counters = await AppDb.Db.Queryable<LabelSerialCounter>()
            .Where(x => x.TemplateId == templateId && x.ImportRowId == importRowId && x.CounterKey == counterKey)
            .Take(1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return counters.FirstOrDefault();
    }

    private static string Format(
        string pattern, 
        DateTime now, 
        long sequence,
        IReadOnlyDictionary<string, string>? rowData)
    {
        return TokenRegex().Replace(pattern, match =>
        {
            var token = match.Groups[1].Value;
            
            // 优先匹配预设的系统时间变量与序列流水号
            switch (token)
            {
                case "yyyy": return now.ToString("yyyy");
                case "yy": return now.ToString("yy");
                case "MM": return now.ToString("MM");
                case "dd": return now.ToString("dd");
                case "HH": return now.ToString("HH");
                case "mm": return now.ToString("mm");
                case "seq": return sequence.ToString();
            }

            if (token.StartsWith("seq:", StringComparison.Ordinal))
            {
                return sequence.ToString(token[4..]);
            }

            // 尝试读取该行数据级绑定的自定义打印变量进行动态替换
            if (rowData != null && rowData.TryGetValue(token, out var val))
            {
                return val ?? string.Empty;
            }

            // 未找到任何匹配时，原样保留占位符本身以起提示作用
            return match.Value;
        });
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TokenRegex();
}
