namespace HarborOps.Modules.Crew.Contracts;

public sealed record VesselManningPlanLineItem(
    Guid Id,
    Guid RankId,
    string RankName,
    int RequiredHeadcount);
