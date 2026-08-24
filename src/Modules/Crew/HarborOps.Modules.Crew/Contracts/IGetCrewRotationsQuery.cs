namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewRotationsQuery
{
    Task<GetCrewRotationsResponse> GetAsync(GetCrewRotationsRequest request, CancellationToken cancellationToken);
}
