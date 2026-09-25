namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewQualificationEligibilityResponse(
    Guid CrewMemberId,
    Guid RankId,
    DateOnly From,
    DateOnly To,
    string Status,
    IReadOnlyList<string> MissingTypes,
    IReadOnlyList<QualificationUncoveredRange> UncoveredDates,
    IReadOnlyList<Guid> CertificateLinks);
