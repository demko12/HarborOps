namespace HarborOps.Modules.Crew.Contracts;

public sealed record VesselManningPlanListItem(
    Guid Id,
    Guid VesselId,
    DateOnly EffectiveStartDate,
    DateOnly EffectiveEndDate,
    int LineCount,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid ConcurrencyToken);
