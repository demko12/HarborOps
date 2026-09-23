namespace HarborOps.Modules.Crew.Contracts;

public sealed record CreateVesselManningPlanResponse(
    Guid Id,
    Guid VesselId,
    DateOnly EffectiveStartDate,
    DateOnly EffectiveEndDate,
    Guid ConcurrencyToken,
    IReadOnlyList<VesselManningPlanLineItem> Lines);
