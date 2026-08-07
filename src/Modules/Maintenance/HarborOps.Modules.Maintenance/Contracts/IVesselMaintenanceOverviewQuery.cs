namespace HarborOps.Modules.Maintenance.Contracts;

public interface IVesselMaintenanceOverviewQuery
{
    Task<VesselMaintenanceOverviewResponse> GetVesselMaintenanceOverviewAsync(
        VesselMaintenanceOverviewRequest request,
        CancellationToken cancellationToken);
}
