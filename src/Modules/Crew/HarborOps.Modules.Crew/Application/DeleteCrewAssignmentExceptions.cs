namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewAssignmentNotFoundException(Guid assignmentId)
    : Exception($"Crew assignment with ID '{assignmentId}' was not found.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}

internal sealed class CrewAssignmentNotDeletableException(Guid assignmentId)
    : Exception($"Crew assignment with ID '{assignmentId}' cannot be deleted because it is not in Planned status.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}
