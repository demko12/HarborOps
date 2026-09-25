namespace HarborOps.Modules.Documents.Contracts;

public sealed record UpdateDocumentRequirementRequest
{
    public bool? IsEnabled { get; init; }

    public int? MinimumRemainingValidityDays { get; init; }

    public string? Instruction { get; init; }

    public string? Reason { get; init; }

    public Guid? Version { get; init; }
}
