namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewCertificate
{
    internal const int TypeMaxLength = 100;
    internal const int NameMaxLength = 200;
    internal const int IssuingAuthorityMaxLength = 200;
    internal const int LicenseNumberMaxLength = 100;
    internal const int StatusMaxLength = 50;
    internal const int RevocationReasonMaxLength = 500;

    private CrewCertificate()
    {
    }

    private CrewCertificate(
        Guid id,
        Guid crewMemberId,
        Guid? predecessorCertificateId,
        string type,
        string name,
        string issuingAuthority,
        string licenseNumber,
        DateOnly issueDate,
        DateOnly expiryDate,
        string status,
        Guid concurrencyToken)
    {
        Id = id;
        CrewMemberId = crewMemberId;
        PredecessorCertificateId = predecessorCertificateId;
        Type = type;
        Name = name;
        IssuingAuthority = issuingAuthority;
        LicenseNumber = licenseNumber;
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        Status = status;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid Id { get; private set; }
    public Guid CrewMemberId { get; private set; }
    public Guid? PredecessorCertificateId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string IssuingAuthority { get; private set; } = null!;
    public string LicenseNumber { get; private set; } = null!;
    public DateOnly IssueDate { get; private set; }
    public DateOnly ExpiryDate { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid ConcurrencyToken { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public string? RevocationReason { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    internal static CrewCertificate Create(
        Guid crewMemberId,
        string type,
        string name,
        string issuingAuthority,
        string licenseNumber,
        DateOnly issueDate,
        DateOnly expiryDate,
        string status)
    {
        return new CrewCertificate(
            Guid.NewGuid(),
            crewMemberId,
            predecessorCertificateId: null,
            type,
            name,
            issuingAuthority,
            licenseNumber,
            issueDate,
            expiryDate,
            status,
            Guid.NewGuid());
    }

    internal static CrewCertificate CreateRenewal(
        Guid predecessorCertificateId,
        Guid crewMemberId,
        string type,
        string name,
        string issuingAuthority,
        string licenseNumber,
        DateOnly issueDate,
        DateOnly expiryDate,
        string status)
    {
        return new CrewCertificate(
            Guid.NewGuid(),
            crewMemberId,
            predecessorCertificateId,
            type,
            name,
            issuingAuthority,
            licenseNumber,
            issueDate,
            expiryDate,
            status,
            Guid.NewGuid());
    }

    internal void Revoke(Guid actorUserId, string reason, DateTime revokedAtUtc)
    {
        if (IsRevoked)
        {
            throw new CrewCertificateAlreadyRevokedException(Id);
        }

        RevokedAtUtc = revokedAtUtc;
        RevokedByUserId = actorUserId;
        RevocationReason = reason;
        ConcurrencyToken = Guid.NewGuid();
    }
}
