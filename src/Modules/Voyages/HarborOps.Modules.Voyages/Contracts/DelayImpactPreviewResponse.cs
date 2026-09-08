namespace HarborOps.Modules.Voyages.Contracts;

public sealed record DelayImpactPreviewResponse(
    Guid VoyageId,
    Guid DelayId,
    int DelayMinutes,
    DateTime PlannedArrival,
    DateTime ProposedArrival,
    int PlannedArrivalDeltaMinutes,
    IReadOnlyList<DelayImpactStopItem> Stops,
    IReadOnlyList<BerthConflictWarningItem> Warnings);
