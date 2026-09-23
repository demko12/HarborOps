namespace HarborOps.Modules.Crew.Contracts;

public interface IGetVesselManningPlanDetailQuery
{
    Task<GetVesselManningPlanDetailResponse> GetAsync(
        Guid vesselManningPlanId,
        CancellationToken cancellationToken);
}
