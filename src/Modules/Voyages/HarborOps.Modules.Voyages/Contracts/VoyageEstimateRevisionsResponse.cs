namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageEstimateRevisionsResponse(
    Guid VoyageId,
    IReadOnlyList<VoyageEstimateStopItem> Stops,
    IReadOnlyList<VoyageEstimateRevisionBatchItem> Revisions);
