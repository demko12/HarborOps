namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewCertificate
{
    internal const int TypeMaxLength = 100;
    internal const int NameMaxLength = 200;
    internal const int IssuingAuthorityMaxLength = 200;
    internal const int LicenseNumberMaxLength = 100;
    internal const int StatusMaxLength = 50;

    private CrewCertificate()
    {
    }

    private CrewCertificate(
        Guid id,
        Guid crewMemberId,
        string type,
        string name,
        string issuingAuthority,
        string licenseNumber,
        DateOnly issueDate,
        DateOnly expiryDate,
        string status)
    {
        Id = id;
        CrewMemberId = crewMemberId;
        Type = type;
        Name = name;
        IssuingAuthority = issuingAuthority;
        LicenseNumber = licenseNumber;
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        Status = status;
    }

    public Guid Id { get; private set; }
    public Guid CrewMemberId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string IssuingAuthority { get; private set; } = null!;
    public string LicenseNumber { get; private set; } = null!;
    public DateOnly IssueDate { get; private set; }
    public DateOnly ExpiryDate { get; private set; }
    public string Status { get; private set; } = null!;

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
            type,
            name,
            issuingAuthority,
            licenseNumber,
            issueDate,
            expiryDate,
            status);
    }
}
