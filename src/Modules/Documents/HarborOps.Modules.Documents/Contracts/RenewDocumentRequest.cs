namespace HarborOps.Modules.Documents.Contracts;

public sealed record RenewDocumentRequest(
    int? DocumentTypeId,
    string? Number,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? IssuingAuthority);
