using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Api.Composition.Voyages;

public sealed record VoyageEstimateRevisionResult(
    Guid RevisionBatchId,
    Guid DelayId,
    string Reason,
    Guid RevisedByUserId,
    DateTime RevisedAtUtc,
    bool AlertCreated,
    IReadOnlyList<EstimateRevisionStopChangeItem> Changes);
