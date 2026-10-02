namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewMembersSummaryResponse(
    int TotalCount,
    int ActiveCount,
    int ExpiredPassportCount,
    int PassportsExpiringSoonCount);
