namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentsRequest
{
    public Guid? VesselId { get; init; }

    public string? Search { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
