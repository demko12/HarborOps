namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentReadinessRequest
{
    public Guid? VesselId { get; init; }

    public DateOnly? RequiredThrough { get; init; }

    public string? Status { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
