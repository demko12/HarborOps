using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetDryDockDetailsQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetDryDockDetailsResponse> GetDetailsAsync(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var dryDock = await dbContext.Set<DryDock>()
            .AsNoTracking()
            .Where(item => item.Id == dryDockId)
            .Select(item => new
            {
                item.Id,
                item.VesselId,
                item.PortId,
                item.StartDate,
                item.EndDate,
                item.PlannedTasks,
                item.ActualCost,
                item.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (dryDock is null)
        {
            throw new DryDockNotFoundException(dryDockId);
        }

        return new GetDryDockDetailsResponse(
            dryDock.Id,
            dryDock.VesselId,
            dryDock.PortId,
            dryDock.StartDate,
            dryDock.EndDate,
            dryDock.PlannedTasks,
            dryDock.ActualCost,
            dryDock.Status.ToString(),
            DryDock.AllowedNextStatusesFor(dryDock.Status)
                .Select(status => status.ToString())
                .ToList(),
            DryDock.IsOverdue(dryDock.Status, dryDock.StartDate, today),
            DryDock.DaysUntilStart(dryDock.Status, dryDock.StartDate, today));
    }
}
