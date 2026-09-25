namespace HarborOps.Modules.Documents.Contracts;

public sealed record CreateDocumentRequirementRequest
{
    public Guid? VesselId { get; init; }

    public int? DocumentTypeId { get; init; }

    public int? MinimumRemainingValidityDays { get; init; }

    public string? Instruction { get; init; }

    public string? Reason { get; init; }
}
