namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetBerthAvailabilityQuery
{
    Task<BerthAvailabilityResponse> GetAsync(Guid portId, BerthAvailabilityRequest request, CancellationToken cancellationToken);
}
