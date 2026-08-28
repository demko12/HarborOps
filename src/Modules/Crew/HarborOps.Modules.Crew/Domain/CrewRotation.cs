namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewRotation
{
    internal const int RotationPatternMaxLength = 100;
    internal const int CancellationNotesMaxLength = 1000;
    internal const int StatusMaxLength = 50;
    internal const string PlannedStatus = "Planned";
    internal const string ExecutedStatus = "Executed";
    internal const string CancelledStatus = "Cancelled";

    private CrewRotation()
    {
    }

    private CrewRotation(
        Guid id,
        Guid assignmentId,
        Guid? relievingCrewMemberId,
        string rotationPattern,
        DateOnly plannedReliefDate)
    {
        Id = id;
        AssignmentId = assignmentId;
        RelievingCrewMemberId = relievingCrewMemberId;
        RotationPattern = rotationPattern;
        PlannedReliefDate = plannedReliefDate;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid? RelievingCrewMemberId { get; private set; }
    public string RotationPattern { get; private set; } = null!;
    public DateOnly PlannedReliefDate { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime? ExecutedAtUtc { get; private set; }
    public Guid? ExecutedByUserId { get; private set; }
    public Guid? ReplacementAssignmentId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancellationNotes { get; private set; }

    internal static CrewRotation Create(
        Guid assignmentId,
        Guid? relievingCrewMemberId,
        string rotationPattern,
        DateOnly plannedReliefDate)
    {
        var rotation = new CrewRotation(
            Guid.NewGuid(),
            assignmentId,
            relievingCrewMemberId,
            rotationPattern,
            plannedReliefDate);
        rotation.Status = PlannedStatus;
        return rotation;
    }

    internal void Update(Guid? relievingCrewMemberId, string rotationPattern, DateOnly plannedReliefDate)
    {
        if (Status != PlannedStatus)
        {
            throw new InvalidCrewRotationTransitionException(Id, Status, PlannedStatus);
        }

        RelievingCrewMemberId = relievingCrewMemberId;
        RotationPattern = rotationPattern;
        PlannedReliefDate = plannedReliefDate;
    }

    internal void Execute(Guid actingUserId, DateTime executedAtUtc, Guid replacementAssignmentId)
    {
        if (Status != PlannedStatus)
        {
            throw new InvalidCrewRotationTransitionException(Id, Status, ExecutedStatus);
        }

        Status = ExecutedStatus;
        ExecutedAtUtc = executedAtUtc;
        ExecutedByUserId = actingUserId;
        ReplacementAssignmentId = replacementAssignmentId;
    }

    internal void Cancel(Guid actingUserId, DateTime cancelledAtUtc, string? notes)
    {
        if (Status != PlannedStatus)
        {
            throw new InvalidCrewRotationTransitionException(Id, Status, CancelledStatus);
        }

        Status = CancelledStatus;
        CancelledAtUtc = cancelledAtUtc;
        CancelledByUserId = actingUserId;
        CancellationNotes = notes;
    }
}
