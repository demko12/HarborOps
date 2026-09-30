namespace HarborOps.Modules.Compliance.Domain;

internal sealed class ComplianceCertificate
{
    internal const int CertificateTypeMaxLength = 100;
    internal const int CertificateNumberMaxLength = 100;
    internal const int IssuingAuthorityMaxLength = 200;
    internal const int RevocationReasonMinLength = 10;
    internal const int RevocationReasonMaxLength = 1000;

    internal const string CurrentStatus = "Current";
    internal const string NotYetEffectiveStatus = "NotYetEffective";
    internal const string ExpiredStatus = "Expired";
    internal const string RevokedStatus = "Revoked";

    internal const string RenewAction = "renew";
    internal const string RevokeAction = "revoke";

    private ComplianceCertificate()
    {
    }

    private ComplianceCertificate(
        Guid id,
        Guid vesselId,
        string certificateType,
        string certificateNumber,
        string issuingAuthority,
        DateOnly issuedOn,
        DateOnly expiresOn,
        DateTime createdUtc,
        Guid? predecessorCertificateId)
    {
        Id = id;
        VesselId = vesselId;
        CertificateType = certificateType;
        CertificateNumber = certificateNumber;
        IssuingAuthority = issuingAuthority;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        CreatedUtc = createdUtc;
        PredecessorCertificateId = predecessorCertificateId;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public string CertificateType { get; private set; } = null!;

    public string CertificateNumber { get; private set; } = null!;

    public string IssuingAuthority { get; private set; } = null!;

    public DateOnly IssuedOn { get; private set; }

    public DateOnly ExpiresOn { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid? PredecessorCertificateId { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public string? RevocationReason { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    internal static ComplianceCertificate Register(
        Guid vesselId,
        string certificateType,
        string certificateNumber,
        string issuingAuthority,
        DateOnly issuedOn,
        DateOnly expiresOn)
    {
        return new ComplianceCertificate(
            Guid.NewGuid(),
            vesselId,
            certificateType,
            certificateNumber,
            issuingAuthority,
            issuedOn,
            expiresOn,
            DateTime.UtcNow,
            predecessorCertificateId: null);
    }

    internal ComplianceCertificate Renew(
        string certificateNumber,
        string issuingAuthority,
        DateOnly issuedOn,
        DateOnly expiresOn,
        DateTime renewedAtUtc)
    {
        if (IsRevoked)
        {
            throw new InvalidComplianceCertificateTransitionException(
                Id, RenewAction, "A revoked certificate cannot be renewed.");
        }

        if (expiresOn < issuedOn || issuedOn < IssuedOn || expiresOn <= ExpiresOn)
        {
            throw new InvalidComplianceCertificateTransitionException(
                Id, RenewAction, "A renewal must not start before the original certificate and must extend its validity.");
        }

        ConcurrencyToken = Guid.NewGuid();

        return new ComplianceCertificate(
            Guid.NewGuid(),
            VesselId,
            CertificateType,
            certificateNumber,
            issuingAuthority,
            issuedOn,
            expiresOn,
            renewedAtUtc,
            Id);
    }

    internal void Revoke(Guid actorUserId, string reason, DateTime revokedAtUtc)
    {
        if (IsRevoked)
        {
            throw new InvalidComplianceCertificateTransitionException(
                Id, RevokeAction, "The certificate is already revoked.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to revoke a certificate.", nameof(reason));
        }

        RevokedAtUtc = revokedAtUtc;
        RevokedByUserId = actorUserId;
        RevocationReason = reason;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal bool IsDirectSuccessorOf(ComplianceCertificate predecessor)
    {
        return PredecessorCertificateId == predecessor.Id
            && Id != predecessor.Id
            && VesselId == predecessor.VesselId
            && string.Equals(CertificateType, predecessor.CertificateType, StringComparison.Ordinal);
    }
}
