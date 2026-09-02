namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewEmploymentStatusHistoryResponse(IReadOnlyList<CrewEmploymentStatusHistoryItem> Items);
