namespace HarborOps.Modules.Crew.Contracts;

public sealed record RevokeCrewCertificateRequest
{
    public string? Reason { get; init; }
}
