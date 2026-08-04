namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewMember
{
    internal const int NameMaxLength = 200;
    internal const int NationalityMaxLength = 100;
    internal const int ImoSeafarerIdMaxLength = 50;
    internal const int PassportNumberMaxLength = 50;
    internal const int CdcNumberMaxLength = 50;
    internal const int EmploymentStatusMaxLength = 50;

    private CrewMember()
    {
    }

    private CrewMember(
        Guid id,
        Guid baseRankId,
        string name,
        DateOnly dateOfBirth,
        string nationality,
        string imoSeafarerId,
        string passportNumber,
        DateOnly passportExpiry,
        string cdcNumber,
        DateOnly hireDate,
        string employmentStatus)
    {
        Id = id;
        BaseRankId = baseRankId;
        Name = name;
        DateOfBirth = dateOfBirth;
        Nationality = nationality;
        ImoSeafarerId = imoSeafarerId;
        PassportNumber = passportNumber;
        PassportExpiry = passportExpiry;
        CdcNumber = cdcNumber;
        HireDate = hireDate;
        EmploymentStatus = employmentStatus;
    }

    public Guid Id { get; private set; }

    public Guid BaseRankId { get; private set; }

    public string Name { get; private set; } = null!;

    public DateOnly DateOfBirth { get; private set; }

    public string Nationality { get; private set; } = null!;

    public string ImoSeafarerId { get; private set; } = null!;

    public string PassportNumber { get; private set; } = null!;

    public DateOnly PassportExpiry { get; private set; }

    public string CdcNumber { get; private set; } = null!;

    public DateOnly HireDate { get; private set; }

    public string EmploymentStatus { get; private set; } = null!;

    internal static CrewMember Create(
        Guid baseRankId,
        string name,
        DateOnly dateOfBirth,
        string nationality,
        string imoSeafarerId,
        string passportNumber,
        DateOnly passportExpiry,
        string cdcNumber,
        DateOnly hireDate,
        string employmentStatus)
    {
        return new CrewMember(
            Guid.NewGuid(),
            baseRankId,
            name,
            dateOfBirth,
            nationality,
            imoSeafarerId,
            passportNumber,
            passportExpiry,
            cdcNumber,
            hireDate,
            employmentStatus);
    }
}
