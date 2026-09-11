namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewAvailabilityConflictsResponse(IReadOnlyList<CrewAvailabilityConflict> Conflicts);
