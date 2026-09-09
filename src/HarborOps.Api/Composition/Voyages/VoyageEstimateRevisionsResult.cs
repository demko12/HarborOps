using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Api.Composition.Voyages;

public sealed record VoyageEstimateRevisionsResult(
    Guid VoyageId,
    IReadOnlyList<VoyageEstimateStopItem> Stops,
    IReadOnlyList<VoyageEstimateRevisionResult> Revisions);
