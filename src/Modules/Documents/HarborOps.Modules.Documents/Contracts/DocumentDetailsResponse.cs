namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentDetailsResponse(
    Guid Id,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    string ReviewStatus,
    bool HasFile,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    Guid? UploadedBy,
    DateTime? UploadedAtUtc);
