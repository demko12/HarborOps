namespace HarborOps.Modules.Crew.Contracts;

public interface IGetVesselManningPlansQuery
{
    Task<GetVesselManningPlansResponse> GetAsync(
        GetVesselManningPlansRequest request,
        CancellationToken cancellationToken);
}
