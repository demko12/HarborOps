namespace HarborOps.Modules.Crew.Contracts;

public sealed record SearchCrewPositionsResponse(IReadOnlyList<CrewPositionListItem> Items);
