using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class OperationsDashboardFleetQuery(DbContext dbContext)
    : IOperationsDashboardFleetQuery
{
    public async Task<OperationsDashboardFleetResult> GetActiveVesselsAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var vessels = dbContext.Set<Vessel>()
            .AsNoTracking()
            .Where(vessel => vessel.CurrentStatus != "Inactive" && vessel.CurrentStatus != "Decommissioned");

        var activeVesselCount = await vessels.CountAsync(cancellationToken);
        var items = await vessels
            .OrderBy(vessel => vessel.Name)
            .ThenBy(vessel => vessel.Id)
            .Take(maximumItems)
            .Select(vessel => new OperationsDashboardFleetItem(
                vessel.Id,
                vessel.Name,
                vessel.CurrentStatus))
            .ToListAsync(cancellationToken);

        return new OperationsDashboardFleetResult(activeVesselCount, items);
    }
}
