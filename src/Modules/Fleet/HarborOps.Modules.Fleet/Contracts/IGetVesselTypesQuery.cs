namespace HarborOps.Modules.Fleet.Contracts;

public interface IGetVesselTypesQuery
{
    Task<VesselTypesResponse> GetAsync(CancellationToken cancellationToken);
}
