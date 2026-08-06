namespace HarborOps.Modules.Compliance.Domain;

internal sealed class ComplianceCertificate
{
    internal const int CertificateTypeMaxLength = 100;
    internal const int CertificateNumberMaxLength = 100;
    internal const int IssuingAuthorityMaxLength = 200;

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
        DateTime createdUtc)
    {
        Id = id;
        VesselId = vesselId;
        CertificateType = certificateType;
        CertificateNumber = certificateNumber;
        IssuingAuthority = issuingAuthority;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        CreatedUtc = createdUtc;
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public string CertificateType { get; private set; } = null!;

    public string CertificateNumber { get; private set; } = null!;

    public string IssuingAuthority { get; private set; } = null!;

    public DateOnly IssuedOn { get; private set; }

    public DateOnly ExpiresOn { get; private set; }

    public DateTime CreatedUtc { get; private set; }

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
            DateTime.UtcNow);
    }
}
