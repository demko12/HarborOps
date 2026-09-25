namespace HarborOps.Modules.Crew.Contracts;

public sealed record CoverageUnqualifiedMember(Guid CrewMemberId, IReadOnlyList<string> MissingTypes);
