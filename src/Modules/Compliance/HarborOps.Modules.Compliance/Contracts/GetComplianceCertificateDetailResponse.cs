namespace HarborOps.Modules.Compliance.Contracts;

public sealed record ComplianceCertificateChainItem(
    Guid Id,
    string CertificateNumber,
    string IssuingAuthority,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    string Status,
    DateTime? RevokedAtUtc,
    Guid? PredecessorCertificateId,
    int? GapDaysAfterPredecessor,
    bool CoversPredecessorExpiry);

public sealed record ComplianceCertificateHistoryItem(
    Guid Id,
    Guid CertificateId,
    string EventType,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    string? Reason,
    Guid? SuccessorCertificateId);

public sealed record GetComplianceCertificateDetailResponse(
    Guid Id,
    Guid VesselId,
    string CertificateType,
    string CertificateNumber,
    string IssuingAuthority,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    string Status,
    DateTime CreatedUtc,
    Guid? PredecessorCertificateId,
    Guid? SuccessorCertificateId,
    DateTime? RevokedAtUtc,
    Guid? RevokedByUserId,
    string? RevocationReason,
    bool CanRenew,
    bool CanRevoke,
    Guid ConcurrencyToken,
    IReadOnlyList<ComplianceCertificateChainItem> Chain,
    IReadOnlyList<ComplianceCertificateHistoryItem> History);
