namespace HarborOps.Modules.Compliance.Contracts;

public interface IReportsComplianceExposureQuery
{
    Task<ReportsComplianceExposureResult> SearchAsync(
        ReportsComplianceExposureRequest request,
        CancellationToken cancellationToken);
}

public sealed record ReportsComplianceExposureRequest
{
    public required DateOnly AsOf { get; init; }

    public Guid? VesselId { get; init; }

    /// <summary>One of "Certificate", "Insurance", "Detention", "NonConformity"; null includes all four.</summary>
    public string? Category { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record ReportsComplianceExposureResult(
    IReadOnlyList<ReportsComplianceExposureRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

public sealed record ReportsComplianceExposureRow(
    string ItemType,
    Guid VesselId,
    string Reference,
    string Description,
    DateOnly RelevantDate,
    string Status,
    string? Severity);
