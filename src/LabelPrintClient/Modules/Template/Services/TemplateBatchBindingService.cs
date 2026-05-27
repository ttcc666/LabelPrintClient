using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.Services;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.Template.Services;

public static class TemplateBatchBindingService
{
    public static async Task<int> ClearLockedBatchNumbersAsync(long templateId, CancellationToken cancellationToken = default)
    {
        var rows = await AppDb.Db.Queryable<LabelImportRow>()
            .Where(x => x.TemplateId == templateId && x.IsPrinted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changedRows = new List<LabelImportRow>();
        foreach (var row in rows)
        {
            var data = JsonHelper.Deserialize<Dictionary<string, string>>(row.RowDataJson) ?? new Dictionary<string, string>();
            if (!data.Remove(TemplateSystemFields.BatchNo))
                continue;

            row.RowDataJson = JsonHelper.Serialize(data);
            row.SearchText = SearchTextBuilder.FromDictionary(data);
            changedRows.Add(row);
        }

        if (changedRows.Count > 0)
            await AppDb.Db.Updateable(changedRows).ExecuteCommandAsync().ConfigureAwait(false);

        return changedRows.Count;
    }
}
