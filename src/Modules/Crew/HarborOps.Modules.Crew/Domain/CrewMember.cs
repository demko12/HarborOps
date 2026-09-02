namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewMember
{
    internal const int FirstNameMaxLength = 100;
    internal const int LastNameMaxLength = 100;
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
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string nationality,
        string imoSeafarerId,
        string passportNumber,
        DateOnly passportExpiry,
        string cdcNumber,
        DateOnly hireDate,
        string employmentStatus,
        DateTime createdUtc)
    {
        Id = id;
        BaseRankId = baseRankId;
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Nationality = nationality;
        ImoSeafarerId = imoSeafarerId;
        PassportNumber = passportNumber;
        PassportExpiry = passportExpiry;
        CdcNumber = cdcNumber;
        HireDate = hireDate;
        EmploymentStatus = employmentStatus;
        CreatedUtc = createdUtc;
    }

    public Guid Id { get; private set; }
    public Guid BaseRankId { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public DateOnly DateOfBirth { get; private set; }
    public string Nationality { get; private set; } = null!;
    public string ImoSeafarerId { get; private set; } = null!;
    public string PassportNumber { get; private set; } = null!;
    public DateOnly PassportExpiry { get; private set; }
    public string CdcNumber { get; private set; } = null!;
    public DateOnly HireDate { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public string EmploymentStatus { get; private set; } = null!;

    internal static CrewMember Create(
        Guid baseRankId,
        string firstName,
        string lastName,
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
            firstName,
            lastName,
            dateOfBirth,
            nationality,
            imoSeafarerId,
            passportNumber,
            passportExpiry,
            cdcNumber,
            hireDate,
            employmentStatus,
            DateTime.UtcNow);
    }

    internal void UpdateProfile(
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string nationality,
        string passportNumber,
        DateOnly passportExpiry,
        string cdcNumber,
        DateOnly hireDate,
        Guid baseRankId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(nationality);
        ArgumentException.ThrowIfNullOrWhiteSpace(passportNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(cdcNumber);

        if (baseRankId == Guid.Empty)
        {
            throw new ArgumentException("Base rank is required.", nameof(baseRankId));
        }

        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Nationality = nationality;
        PassportNumber = passportNumber;
        PassportExpiry = passportExpiry;
        CdcNumber = cdcNumber;
        HireDate = hireDate;
        BaseRankId = baseRankId;
    }

    internal void ChangeEmploymentStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        EmploymentStatus = status;
    }
}
