namespace HarborOps.Modules.Crew.Contracts;

public sealed record CompleteCrewAssignmentRequest
{
    public DateOnly? ActualSignOffDate { get; init; }

    public Guid? SignOffPortId { get; init; }
}
