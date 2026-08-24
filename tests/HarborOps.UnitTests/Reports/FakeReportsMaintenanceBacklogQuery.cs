using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Reports;

internal sealed class FakeReportsMaintenanceBacklogQuery(ReportsMaintenanceBacklogResult response) : IReportsMaintenanceBacklogQuery
{
    internal int CallCount { get; private set; }

    internal ReportsMaintenanceBacklogRequest? LastRequest { get; private set; }

    public Task<ReportsMaintenanceBacklogResult> SearchAsync(
        ReportsMaintenanceBacklogRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;

        return Task.FromResult(response);
    }
}
