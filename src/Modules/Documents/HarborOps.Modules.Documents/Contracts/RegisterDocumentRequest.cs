namespace HarborOps.Modules.Documents.Contracts;

public sealed record RegisterDocumentRequest
{
    public Guid? VesselId { get; init; }

    public int? DocumentTypeId { get; init; }

    public string? Number { get; init; }

    public DateOnly? IssueDate { get; init; }

    public DateOnly? ExpiryDate { get; init; }

    public string? IssuingAuthority { get; init; }

    public string? Status { get; init; }
}
