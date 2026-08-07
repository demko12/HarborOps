using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Operations;

internal sealed class FakeVesselMaintenanceOverviewQuery(
    params VesselMaintenanceOverviewSummary[] summaries)
    : IVesselMaintenanceOverviewQuery
{
    internal int CallCount { get; private set; }

    internal IReadOnlyCollection<Guid>? LastVesselIds { get; private set; }

    public Task<VesselMaintenanceOverviewResponse> GetVesselMaintenanceOverviewAsync(
        VesselMaintenanceOverviewRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastVesselIds = request.VesselIds;

        var requestedVesselIds = request.VesselIds ?? [];
        IReadOnlyCollection<VesselMaintenanceOverviewSummary> matches = summaries
            .Where(summary => requestedVesselIds.Contains(summary.VesselId))
            .ToArray();

        return Task.FromResult(new VesselMaintenanceOverviewResponse(matches));
    }
}
