namespace HarborOps.Modules.Crew.Contracts;

public sealed record RegisterCrewCertificateRequest
{
    public string? Type { get; init; }
    public string? Name { get; init; }
    public string? IssuingAuthority { get; init; }
    public string? LicenseNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
}
