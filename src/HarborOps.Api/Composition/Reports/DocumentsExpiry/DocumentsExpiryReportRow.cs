namespace HarborOps.Api.Composition.Reports.DocumentsExpiry;

public sealed record DocumentsExpiryReportRow(
    Guid DocumentId,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    int DaysUntilExpiry,
    bool IsExpired);
