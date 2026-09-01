namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentRenewalHistoryItem(
    Guid DocumentId,
    Guid? PredecessorDocumentId,
    string Number,
    string Status,
    DateOnly ExpiryDate,
    DateTime? SupersededAtUtc,
    Guid? SupersededByUserId);
