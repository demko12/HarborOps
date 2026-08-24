using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class ScheduleDryDockService(DbContext dbContext)
{
    public async Task<ScheduleDryDockResponse> ScheduleAsync(
        ScheduleDryDockRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ScheduleDryDockValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ScheduleDryDockValidationException(errors);
        }

        var dryDock = DryDock.Schedule(
            request.VesselId!.Value,
            request.PortId!.Value,
            request.StartDate!.Value,
            request.PlannedTasks!.Trim());

        await dbContext.Set<DryDock>().AddAsync(dryDock, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ScheduleDryDockResponse(dryDock.Id);
    }
}
