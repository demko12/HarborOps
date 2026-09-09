namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetVoyageEstimateRevisionsQuery
{
    Task<VoyageEstimateRevisionsResponse> GetEstimateRevisionsAsync(
        Guid voyageId,
        CancellationToken cancellationToken);
}
