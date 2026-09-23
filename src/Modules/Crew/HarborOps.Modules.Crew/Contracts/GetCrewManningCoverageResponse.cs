namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewManningCoverageResponse(
    Guid VesselId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CoverageDayItem> Days,
    IReadOnlyList<CoverageShortageInterval> ShortageIntervals);
