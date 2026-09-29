namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewCertificateProfileItem(
    Guid CertificateId,
    string Type,
    string Name,
    string IssuingAuthority,
    string LicenseNumber,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string Status,
    bool IsRevoked,
    Guid? PredecessorCertificateId,
    Guid? SuccessorCertificateId,
    Guid ConcurrencyToken);
