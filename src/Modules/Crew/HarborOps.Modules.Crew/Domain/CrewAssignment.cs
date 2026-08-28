namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewAssignment
{
    internal const int StatusMaxLength = 50;
    internal const int CancellationNotesMaxLength = 1000;
    internal const string PlannedStatus = "Planned";
    internal const string ActiveStatus = "Active";
    internal const string CompletedStatus = "Completed";
    internal const string CancelledStatus = "Cancelled";
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
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancellationNotes { get; private set; }
    public string Status { get; private set; } = null!;

    internal static CrewAssignment CreatePlanned(
        Guid crewMemberId,
        Guid vesselId,
        Guid rankId,
        Guid? signOnPortId,
        DateOnly signOnDate,
        DateOnly plannedSignOffDate)
    {
        return new CrewAssignment(
            Guid.NewGuid(),
            crewMemberId,
            vesselId,
            rankId,
            signOnPortId,
            signOffPortId: null,
            signOnDate,
            plannedSignOffDate,
            actualSignOffDate: null,
            PlannedStatus);
    }

    internal void Activate()
    {
        if (Status != PlannedStatus)
        {
            throw new InvalidCrewAssignmentTransitionException(Id, Status, ActiveStatus);
        }

        Status = ActiveStatus;
    }

    internal void Complete(DateOnly actualSignOffDate, Guid? signOffPortId)
    {
        if (Status is not (PlannedStatus or ActiveStatus))
        {
            throw new InvalidCrewAssignmentTransitionException(Id, Status, CompletedStatus);
        }

        if (actualSignOffDate < SignOnDate)
        {
            throw new InvalidCrewAssignmentDateException(Id, actualSignOffDate, SignOnDate);
        }

        ActualSignOffDate = actualSignOffDate;
        SignOffPortId = signOffPortId;
        Status = CompletedStatus;
    }

    internal void SignOff(DateOnly actualSignOffDate) => Complete(actualSignOffDate, signOffPortId: null);

    internal void Cancel(Guid actingUserId, DateTime cancelledAtUtc, string? notes)
    {
        if (Status is not (PlannedStatus or ActiveStatus))
        {
            throw new InvalidCrewAssignmentTransitionException(Id, Status, CancelledStatus);
        }

        Status = CancelledStatus;
        CancelledAtUtc = cancelledAtUtc;
        CancelledByUserId = actingUserId;
        CancellationNotes = notes;
    }
}
