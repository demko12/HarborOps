namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewPositionListItem(
    Guid CrewPositionId,
    string RankName,
    string Department,
    string SeniorityLevel,
    int CrewMemberUsageCount,
    int AssignmentUsageCount);
