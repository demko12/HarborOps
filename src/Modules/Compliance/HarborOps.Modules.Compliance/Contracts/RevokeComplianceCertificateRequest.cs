namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RevokeComplianceCertificateRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? Reason { get; init; }
}

public sealed record RevokeComplianceCertificateResponse(
    Guid Id,
    DateTime RevokedAtUtc,
    Guid RevokedByUserId);
