namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetExpiringDocumentsResponse(
    IReadOnlyList<ExpiringDocumentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
