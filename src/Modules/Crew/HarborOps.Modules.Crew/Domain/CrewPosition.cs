namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewPosition
{
    internal const int RankNameMaxLength = 100;
    internal const int DepartmentMaxLength = 100;
    internal const int SeniorityLevelMaxLength = 50;

    private CrewPosition()
    {
    }

    private CrewPosition(
        Guid id,
        string rankName,
        string department,
        string seniorityLevel)
    {
        Id = id;
        RankName = rankName;
        Department = department;
        SeniorityLevel = seniorityLevel;
    }

    public Guid Id { get; private set; }

    public string RankName { get; private set; } = null!;

    public string Department { get; private set; } = null!;

    public string SeniorityLevel { get; private set; } = null!;

    internal static CrewPosition Create(
        string rankName,
        string department,
        string seniorityLevel)
    {
        return new CrewPosition(Guid.NewGuid(), rankName, department, seniorityLevel);
    }
}
