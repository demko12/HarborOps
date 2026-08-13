namespace HarborOps.Modules.Crew.Contracts;

public sealed record SearchCrewMembersResponse(
    IReadOnlyList<CrewMemberListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
