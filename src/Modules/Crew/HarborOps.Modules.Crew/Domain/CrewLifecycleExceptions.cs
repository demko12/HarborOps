namespace HarborOps.Modules.Crew.Domain;

internal sealed class InvalidCrewAssignmentTransitionException(
    Guid assignmentId,
    string currentStatus,
    string requestedStatus)
    : Exception($"Crew assignment '{assignmentId}' cannot transition from '{currentStatus}' to '{requestedStatus}'.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}

internal sealed class InvalidCrewAssignmentDateException(
    Guid assignmentId,
    DateOnly actualSignOffDate,
    DateOnly signOnDate)
    : Exception($"Crew assignment '{assignmentId}' cannot sign off on '{actualSignOffDate:yyyy-MM-dd}' before sign-on date '{signOnDate:yyyy-MM-dd}'.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}

internal sealed class InvalidCrewRotationTransitionException(
    Guid rotationId,
    string currentStatus,
    string requestedStatus)
    : Exception($"Crew rotation '{rotationId}' cannot transition from '{currentStatus}' to '{requestedStatus}'.")
{
    internal Guid RotationId { get; } = rotationId;
}
