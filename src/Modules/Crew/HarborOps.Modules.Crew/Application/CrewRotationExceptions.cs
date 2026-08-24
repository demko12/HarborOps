namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewRotationValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Crew rotation validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CrewRotationNotFoundException(Guid assignmentId)
    : Exception($"No crew rotation is planned for assignment '{assignmentId}'.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}

internal sealed class DuplicateCrewRotationException(Guid assignmentId)
    : Exception($"Crew assignment '{assignmentId}' already has a planned rotation.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}

internal sealed class CrewAssignmentNotEligibleForRotationException(Guid assignmentId)
    : Exception($"Crew assignment '{assignmentId}' is not eligible for a rotation.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}

internal sealed class SelfRelievingCrewMemberException(Guid crewMemberId)
    : Exception($"Crew member '{crewMemberId}' cannot relieve themselves.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
}

internal sealed class RelievingCrewMemberUnavailableException(Guid crewMemberId)
    : Exception($"Crew member '{crewMemberId}' already has an open assignment and is not available to relieve.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
}

internal sealed class CrewRotationNotExecutableException(Guid assignmentId)
    : Exception($"Crew rotation for assignment '{assignmentId}' cannot be executed because no relieving crew member is selected.")
{
    internal Guid AssignmentId { get; } = assignmentId;
}
