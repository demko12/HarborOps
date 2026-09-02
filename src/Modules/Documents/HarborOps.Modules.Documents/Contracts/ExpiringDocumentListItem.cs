namespace HarborOps.Modules.Documents.Contracts;

public sealed record ExpiringDocumentListItem(
    Guid DocumentId,
    Guid VesselId,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    string ReviewStatus,
    int DocumentTypeId,
    string DocumentTypeName,
    int DaysUntilExpiry);
