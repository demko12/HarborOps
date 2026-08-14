namespace HarborOps.Modules.Compliance.Contracts;

public interface IGetVesselComplianceOverviewQuery
{
    Task<GetVesselComplianceOverviewResponse> GetAsync(
        GetVesselComplianceOverviewRequest request,
        CancellationToken cancellationToken);
}
