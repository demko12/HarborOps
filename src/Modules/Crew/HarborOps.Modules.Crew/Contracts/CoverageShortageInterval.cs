namespace HarborOps.Modules.Crew.Contracts;

public sealed record CoverageShortageInterval(
    Guid RankId,
    string RankName,
    DateOnly StartDate,
    DateOnly EndDate,
    int RequiredHeadcount,
    int Shortage);
