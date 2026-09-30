namespace HarborOps.Modules.Compliance.Contracts;

public sealed record ComplianceCertificateOverviewItem(
    Guid Id,
    Guid VesselId,
    string CertificateType,
    string CertificateNumber,
    string IssuingAuthority,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    string Status,
    Guid? PredecessorCertificateId,
    DateTime? RevokedAtUtc,
    Guid? SuccessorCertificateId,
    string? SuccessorCertificateNumber,
    DateOnly? SuccessorIssuedOn,
    bool SuccessorIsRevoked,
    bool IsRenewalCovered,
    int? RenewalGapDays);
