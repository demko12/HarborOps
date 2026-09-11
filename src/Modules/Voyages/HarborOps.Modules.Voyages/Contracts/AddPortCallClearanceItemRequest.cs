namespace HarborOps.Modules.Voyages.Contracts;

public sealed record AddPortCallClearanceItemRequest
{
    public string? Category { get; init; }

    public string? Title { get; init; }

    public bool IsRequired { get; init; }

    public DateTime? DueAtUtc { get; init; }

    public string? AuthorityReference { get; init; }

    public Guid? EvidenceDocumentId { get; init; }
}
