namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageEstimateRevisionBatchItem(
    Guid RevisionBatchId,
    Guid DelayId,
    string Reason,
    Guid RevisedByUserId,
    DateTime RevisedAtUtc,
    IReadOnlyList<EstimateRevisionStopChangeItem> Changes);
