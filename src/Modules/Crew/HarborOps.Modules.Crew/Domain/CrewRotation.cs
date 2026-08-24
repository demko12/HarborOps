namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewRotation
{
    internal const int RotationPatternMaxLength = 100;

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

    internal static CrewRotation Create(
        Guid assignmentId,
        Guid? relievingCrewMemberId,
        string rotationPattern,
        DateOnly plannedReliefDate)
    {
        return new CrewRotation(
            Guid.NewGuid(),
            assignmentId,
            relievingCrewMemberId,
            rotationPattern,
            plannedReliefDate);
    }

    internal void Update(Guid? relievingCrewMemberId, string rotationPattern, DateOnly plannedReliefDate)
    {
        RelievingCrewMemberId = relievingCrewMemberId;
        RotationPattern = rotationPattern;
        PlannedReliefDate = plannedReliefDate;
    }
}
