namespace HarborOps.Modules.Crew.Application;

internal sealed class UpdateCrewMemberValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update crew member validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class ChangeCrewMemberEmploymentStatusValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Change crew member employment status validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CrewMemberEmploymentStatusUnchangedException(Guid crewMemberId, string status)
    : Exception($"Crew member '{crewMemberId}' already has employment status '{status}'.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
    internal string Status { get; } = status;
}

internal sealed class CrewMemberHasOpenAssignmentOrRotationException(Guid crewMemberId)
    : Exception($"Crew member '{crewMemberId}' cannot be terminated while an open assignment or planned rotation exists.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
}
