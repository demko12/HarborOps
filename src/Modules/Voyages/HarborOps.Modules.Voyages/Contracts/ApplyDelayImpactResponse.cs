namespace HarborOps.Modules.Voyages.Contracts;

public sealed record ApplyDelayImpactResponse(
    Guid RevisionBatchId,
    Guid VoyageId,
    Guid DelayId,
    string Reason,
    Guid RevisedByUserId,
    DateTime RevisedAtUtc,
    IReadOnlyList<EstimateRevisionStopChangeItem> Changes);
