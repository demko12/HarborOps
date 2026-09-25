namespace HarborOps.Modules.Documents.Contracts;

public interface IGetVesselReadinessOverviewQuery
{
    Task<GetVesselReadinessOverviewResponse> GetAsync(
        GetVesselReadinessOverviewRequest request,
        CancellationToken cancellationToken);
}
