namespace HarborOps.Modules.Crew.Contracts;

/// <summary>
/// A single scheduling conflict for a crew member over a requested date window. Carries only the
/// record type, status and date range — never another crew member's private reason or notes.
/// </summary>
public sealed record CrewAvailabilityConflict(
    string RecordType,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Detail);
