namespace HarborOps.Modules.Crew.Contracts;

public sealed record CoverageIneligibleMember(
    Guid CrewMemberId,
    string Reason);
