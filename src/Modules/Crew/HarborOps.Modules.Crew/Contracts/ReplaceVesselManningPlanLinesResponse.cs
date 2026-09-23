namespace HarborOps.Modules.Crew.Contracts;

public sealed record ReplaceVesselManningPlanLinesResponse(
    Guid Id,
    Guid ConcurrencyToken,
    IReadOnlyList<VesselManningPlanLineItem> Lines);
