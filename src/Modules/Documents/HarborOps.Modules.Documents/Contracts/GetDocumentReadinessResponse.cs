namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentReadinessResponse(
    Guid? VesselId,
    string? VesselStatus,
    DateOnly Today,
    DateOnly RequiredThrough,
    DocumentReadinessSummary Summary,
    IReadOnlyList<DocumentReadinessItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
