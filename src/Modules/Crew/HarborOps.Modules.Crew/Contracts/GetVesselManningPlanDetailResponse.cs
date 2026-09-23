namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetVesselManningPlanDetailResponse(
    Guid Id,
    Guid VesselId,
    DateOnly EffectiveStartDate,
    DateOnly EffectiveEndDate,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid ConcurrencyToken,
    IReadOnlyList<VesselManningPlanLineItem> Lines,
    IReadOnlyList<VesselManningPlanHistoryItem> History);
