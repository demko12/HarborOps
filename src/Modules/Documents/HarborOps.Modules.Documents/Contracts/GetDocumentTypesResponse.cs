namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentTypesResponse(
    IReadOnlyList<DocumentTypeListItem> Items,
    int PageSize,
    int TotalCount);
