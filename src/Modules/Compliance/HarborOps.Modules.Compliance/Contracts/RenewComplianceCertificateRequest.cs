namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RenewComplianceCertificateRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? CertificateNumber { get; init; }

    public string? IssuingAuthority { get; init; }

    public DateOnly? IssuedOn { get; init; }

    public DateOnly? ExpiresOn { get; init; }
}

public sealed record RenewComplianceCertificateResponse(
    Guid Id,
    Guid PredecessorCertificateId);
