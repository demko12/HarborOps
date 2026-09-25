namespace HarborOps.Modules.Documents.Contracts;

public sealed record DisableDocumentRequirementRequest
{
    public string? Reason { get; init; }

    public Guid? Version { get; init; }
}
