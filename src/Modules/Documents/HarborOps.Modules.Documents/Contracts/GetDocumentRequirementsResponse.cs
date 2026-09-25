namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentRequirementsResponse(
    IReadOnlyList<DocumentRequirementListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
