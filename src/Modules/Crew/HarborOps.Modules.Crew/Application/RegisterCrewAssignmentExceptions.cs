namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewMemberNotFoundException(Guid crewMemberId)
    : Exception($"Crew member with ID '{crewMemberId}' was not found.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
}

internal sealed class OpenCrewAssignmentExistsException(Guid crewMemberId)
    : Exception($"Crew member with ID '{crewMemberId}' already has an open assignment.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
}

internal sealed class RegisterCrewAssignmentValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register crew assignment validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
