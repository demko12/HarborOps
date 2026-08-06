namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RegisterComplianceCertificateRequest
{
    public Guid? VesselId { get; init; }

    public string? CertificateType { get; init; }

    public string? CertificateNumber { get; init; }

    public string? IssuingAuthority { get; init; }

    public DateOnly? IssuedOn { get; init; }

    public DateOnly? ExpiresOn { get; init; }
}
