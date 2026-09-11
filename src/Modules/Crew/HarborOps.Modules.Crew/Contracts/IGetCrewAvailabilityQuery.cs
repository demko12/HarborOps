namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewAvailabilityQuery
{
    Task<GetCrewAvailabilityResponse> GetAsync(GetCrewAvailabilityRequest request, CancellationToken cancellationToken);
}
