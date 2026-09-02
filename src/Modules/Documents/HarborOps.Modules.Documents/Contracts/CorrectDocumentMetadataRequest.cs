namespace HarborOps.Modules.Documents.Contracts;

public sealed record CorrectDocumentMetadataRequest(
    int? DocumentTypeId,
    string? Number,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? IssuingAuthority);
