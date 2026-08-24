namespace HarborOps.Modules.Documents.Contracts;

public interface IReportsDocumentsExpiryQuery
{
    Task<ReportsDocumentsExpiryResult> SearchAsync(
        ReportsDocumentsExpiryRequest request,
        CancellationToken cancellationToken);
}

public sealed record ReportsDocumentsExpiryRequest
{
    public required DateOnly AsOf { get; init; }

    public Guid? VesselId { get; init; }

    public int? DocumentTypeId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public string? Status { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record ReportsDocumentsExpiryResult(
    IReadOnlyList<ReportsDocumentsExpiryRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

public sealed record ReportsDocumentsExpiryRow(
    Guid DocumentId,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    int DaysUntilExpiry,
    bool IsExpired);
