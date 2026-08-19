namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentsResponse(
    IReadOnlyList<DocumentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
