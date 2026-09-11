namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallClearanceItemModel(
    Guid ItemId,
    string Category,
    string Title,
    bool IsRequired,
    DateTime? DueAtUtc,
    string? AuthorityReference,
    Guid? EvidenceDocumentId,
    string Status,
    bool IsOverdue,
    string? DecisionReason,
    Guid? DecidedByUserId,
    DateTime? DecidedAtUtc,
    Guid AddedByUserId,
    DateTime AddedAtUtc);
