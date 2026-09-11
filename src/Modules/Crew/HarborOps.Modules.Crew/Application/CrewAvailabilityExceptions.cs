namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewAvailabilityRequestNotFoundException(Guid requestId)
    : Exception($"Crew availability request with ID '{requestId}' was not found.")
{
    internal Guid RequestId { get; } = requestId;
}

internal sealed class CreateCrewAvailabilityRequestValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Create crew availability request validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RejectCrewAvailabilityRequestValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Reject crew availability request validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CancelCrewAvailabilityRequestValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cancel crew availability request validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetCrewAvailabilityValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get crew availability validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetCrewAvailabilityConflictsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get crew availability conflicts validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class OverlappingCrewAvailabilityRequestException(
    Guid crewMemberId,
    DateOnly startDate,
    DateOnly endDate)
    : Exception(
        $"Crew member '{crewMemberId}' already has a requested or approved unavailability period "
        + $"overlapping {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;

    internal DateOnly StartDate { get; } = startDate;

    internal DateOnly EndDate { get; } = endDate;
}

internal sealed class CrewAvailabilitySelfApprovalException(Guid requestId)
    : Exception($"Crew availability request '{requestId}' cannot be approved by the crew member who requested it.")
{
    internal Guid RequestId { get; } = requestId;
}

internal sealed class CrewAvailabilityApprovalConflictException(
    Guid crewMemberId,
    DateOnly conflictStartDate,
    DateOnly conflictEndDate,
    string recordType)
    : Exception(
        $"Crew member '{crewMemberId}' is committed to a {recordType} from "
        + $"{conflictStartDate:yyyy-MM-dd} to {conflictEndDate:yyyy-MM-dd}; the unavailability cannot be approved.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;

    internal DateOnly ConflictStartDate { get; } = conflictStartDate;

    internal DateOnly ConflictEndDate { get; } = conflictEndDate;

    internal string RecordType { get; } = recordType;
}

internal sealed class CrewMemberUnavailableException(
    Guid crewMemberId,
    DateOnly conflictStartDate,
    DateOnly conflictEndDate,
    string recordType)
    : Exception(
        $"Crew member '{crewMemberId}' is unavailable from {conflictStartDate:yyyy-MM-dd} to "
        + $"{conflictEndDate:yyyy-MM-dd} ({recordType}).")
{
    internal Guid CrewMemberId { get; } = crewMemberId;

    internal DateOnly ConflictStartDate { get; } = conflictStartDate;

    internal DateOnly ConflictEndDate { get; } = conflictEndDate;

    internal string RecordType { get; } = recordType;
}
