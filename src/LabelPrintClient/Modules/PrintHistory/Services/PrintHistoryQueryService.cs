using LabelPrintClient.Database;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.PrintCenter.ViewModels;
using LabelPrintClient.Modules.Template.Models;
using SqlSugar;

namespace LabelPrintClient.Modules.PrintHistory.Services;

public sealed class PrintHistoryQueryService
{
    public async Task<PagedResult<PrintJobGridItem>> QueryJobsAsync(
        string? status,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var jobQuery = AppDb.Db.Queryable<LabelPrintJob>();
        if (!string.IsNullOrWhiteSpace(status))
            jobQuery = jobQuery.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            jobQuery = jobQuery.Where(x =>
                x.TemplateName.Contains(keyword) ||
                (x.PrinterName != null && x.PrinterName.Contains(keyword)) ||
                (x.OperatorName != null && x.OperatorName.Contains(keyword)) ||
                (x.ErrorMessage != null && x.ErrorMessage.Contains(keyword)));
        }

        RefAsync<int> totalRowsRef = 0;
        var currentPage = Math.Max(1, page);
        var dbJobs = await jobQuery
            .OrderByDescending(x => x.CreateTime)
            .OrderByDescending(x => x.Id)
            .ToPageListAsync(currentPage, pageSize, totalRowsRef)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new PagedResult<PrintJobGridItem>(
            dbJobs.Select(PrintJobGridItem.From).ToList(),
            totalRowsRef.Value,
            currentPage,
            pageSize);
    }

    public async Task<PrintHistoryRowsResult> QueryRowsAsync(
        long printJobId,
        long templateId,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == templateId)
            .OrderBy(x => x.Sort)
            .ToListAsync()
            .ConfigureAwait(false);

        var rowQuery = AppDb.Db.Queryable<LabelPrintJobRow>()
            .Where(x => x.PrintJobId == printJobId);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            if (int.TryParse(keyword, out var rowIndex))
                rowQuery = rowQuery.Where(x =>
                    x.RowIndex == rowIndex ||
                    (x.SearchText != null && x.SearchText.Contains(keyword)));
            else
                rowQuery = rowQuery.Where(x =>
                    x.SearchText != null && x.SearchText.Contains(keyword));
        }

        RefAsync<int> totalRowsRef = 0;
        var currentPage = Math.Max(1, page);
        var dbRows = await rowQuery
            .OrderBy(x => x.RowIndex)
            .ToPageListAsync(currentPage, pageSize, totalRowsRef)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = dbRows.Select(x => new PrintJobRowGridItem
        {
            Id = x.Id,
            ImportRowId = x.ImportRowId,
            RowIndex = x.RowIndex,
            Data = GridRowDataHelper.Deserialize(x.RowDataJson)
        }).ToList();

        var activeFields = fields.Where(f => !f.IsDeleted && !TemplateSystemFields.IsSystemField(f.FieldCode)).ToList();
        GridRowDataHelper.EnsureFieldKeys(rows.Select(x => x.Data), activeFields);

        var activeCodes = activeFields
            .Select(x => x.FieldCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extraKeys = rows
            .SelectMany(x => x.Data.Keys)
            .Where(x => !activeCodes.Contains(x))
            .Where(x => !TemplateSystemFields.IsSystemField(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        var systemKeys = rows
            .SelectMany(x => x.Data.Keys)
            .Where(TemplateSystemFields.IsSystemField)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetSystemFieldSort)
            .ToList();

        GridRowDataHelper.EnsureKeys(rows.Select(x => x.Data), extraKeys);
        GridRowDataHelper.EnsureKeys(rows.Select(x => x.Data), systemKeys);

        return new PrintHistoryRowsResult(
            new PagedResult<PrintJobRowGridItem>(rows, totalRowsRef.Value, currentPage, pageSize),
            fields,
            extraKeys,
            systemKeys);
    }

    public static int GetSystemFieldSort(string fieldCode)
    {
        if (string.Equals(fieldCode, TemplateSystemFields.BatchNo, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(fieldCode, TemplateSystemFields.SerialNo, StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalRows, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (TotalRows + PageSize - 1) / PageSize);
}

public sealed record PrintHistoryRowsResult(
    PagedResult<PrintJobRowGridItem> Rows,
    IReadOnlyList<LabelTemplateField> Fields,
    IReadOnlyList<string> ExtraKeys,
    IReadOnlyList<string> SystemKeys);
