using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class StartMaintenanceWorkOrderService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<MaintenanceWorkOrderTransitionResponse> StartAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.Set<MaintenanceWorkOrder>()
            .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        workOrder.Start();

        await dbContext.SaveChangesAsync(cancellationToken);

        var transitionedUtc = timeProvider.GetUtcNow().UtcDateTime;

        return new MaintenanceWorkOrderTransitionResponse(
        workOrder.Id,
        workOrder.Status.ToString(),
        workOrder.AllowedActions.Select(action => action.ToString()).ToList(),
        workOrder.CompletedUtc,
        transitionedUtc);
    }
}
