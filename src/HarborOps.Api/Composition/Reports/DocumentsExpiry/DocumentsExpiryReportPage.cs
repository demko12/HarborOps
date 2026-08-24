namespace HarborOps.Api.Composition.Reports.DocumentsExpiry;

public sealed record DocumentsExpiryReportPage(
    DateOnly AsOf,
    IReadOnlyList<DocumentsExpiryReportRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
