namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetVoyageItineraryQuery
{
    Task<VoyageItineraryResponse> GetItineraryAsync(
        Guid voyageId,
        CancellationToken cancellationToken);
}
