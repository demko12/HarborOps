namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentRequirementsRequest
{
    public Guid? VesselId { get; init; }

    public int? DocumentTypeId { get; init; }

    public bool? IsEnabled { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
