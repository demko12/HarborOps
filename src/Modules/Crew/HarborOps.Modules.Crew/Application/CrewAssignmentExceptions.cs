namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewAssignmentNotFoundException(Guid assignmentId)
    : Exception($"Crew assignment with ID '{assignmentId}' was not found.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}
