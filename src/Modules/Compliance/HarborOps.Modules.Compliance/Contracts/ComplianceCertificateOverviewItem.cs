namespace HarborOps.Modules.Compliance.Contracts;

public sealed record ComplianceCertificateOverviewItem(
    Guid Id,
    Guid VesselId,
    string CertificateType,
    string CertificateNumber,
    string IssuingAuthority,
    DateOnly IssuedOn,
    DateOnly ExpiresOn);
