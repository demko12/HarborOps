namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewMemberAvailabilityResponse(IReadOnlyList<CrewMemberAvailabilityItem> Items);
