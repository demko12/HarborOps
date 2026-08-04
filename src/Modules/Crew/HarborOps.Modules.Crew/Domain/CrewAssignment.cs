namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewAssignment
{
    internal const int StatusMaxLength = 50;

    private CrewAssignment()
    {
    }

    private CrewAssignment(
        Guid id,
        Guid crewMemberId,
        Guid vesselId,
        Guid rankId,
        Guid? signOnPortId,
        Guid? signOffPortId,
        DateOnly signOnDate,
        DateOnly plannedSignOffDate,
        DateOnly? actualSignOffDate,
        string status)
    {
        Id = id;
        CrewMemberId = crewMemberId;
        VesselId = vesselId;
        RankId = rankId;
        SignOnPortId = signOnPortId;
        SignOffPortId = signOffPortId;
        SignOnDate = signOnDate;
        PlannedSignOffDate = plannedSignOffDate;
        ActualSignOffDate = actualSignOffDate;
        Status = status;
    }

    public Guid Id { get; private set; }
    public Guid CrewMemberId { get; private set; }
    public Guid VesselId { get; private set; }
    public Guid RankId { get; private set; }
    public Guid? SignOnPortId { get; private set; }
    public Guid? SignOffPortId { get; private set; }
    public DateOnly SignOnDate { get; private set; }
    public DateOnly PlannedSignOffDate { get; private set; }
    public DateOnly? ActualSignOffDate { get; private set; }
    public string Status { get; private set; } = null!;

    internal static CrewAssignment Create(
        Guid crewMemberId,
        Guid vesselId,
        Guid rankId,
        Guid? signOnPortId,
        Guid? signOffPortId,
        DateOnly signOnDate,
        DateOnly plannedSignOffDate,
        DateOnly? actualSignOffDate,
        string status)
    {
        return new CrewAssignment(
            Guid.NewGuid(),
            crewMemberId,
            vesselId,
            rankId,
            signOnPortId,
            signOffPortId,
            signOnDate,
            plannedSignOffDate,
            actualSignOffDate,
            status);
    }
}
