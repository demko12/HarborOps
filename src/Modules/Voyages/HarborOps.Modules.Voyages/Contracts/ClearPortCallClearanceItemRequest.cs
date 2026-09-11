namespace HarborOps.Modules.Voyages.Contracts;

public sealed record ClearPortCallClearanceItemRequest
{
    public string? AuthorityReference { get; init; }

    public Guid? EvidenceDocumentId { get; init; }
}
