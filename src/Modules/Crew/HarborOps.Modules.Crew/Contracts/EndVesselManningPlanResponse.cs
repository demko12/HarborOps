namespace HarborOps.Modules.Crew.Contracts;

public sealed record EndVesselManningPlanResponse(
    Guid Id,
    DateOnly EffectiveEndDate,
    Guid ConcurrencyToken);
