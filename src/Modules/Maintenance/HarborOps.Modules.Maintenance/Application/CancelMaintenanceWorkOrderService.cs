using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CancelMaintenanceWorkOrderService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<MaintenanceWorkOrderTransitionResponse> CancelAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.Set<MaintenanceWorkOrder>()
            .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }
        var transitionedUtc = timeProvider.GetUtcNow().UtcDateTime;

        workOrder.Cancel();

        if (workOrder.SourceScheduleId is { } scheduleId
            && workOrder.SourceOccurrenceDueDate is { } dueDate)
        {
            var occurrence = await dbContext.Set<ScheduleOccurrence>()
                .SingleOrDefaultAsync(
                    candidate => candidate.ScheduleId == scheduleId && candidate.DueDate == dueDate,
                    cancellationToken);

            if (occurrence is not null && occurrence.IsLinkedTo(workOrder.Id))
            {
                occurrence.MarkCancelled(workOrder.Id, transitionedUtc);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MaintenanceWorkOrderTransitionResponse(
            workOrder.Id,
            workOrder.Status.ToString(),
            workOrder.AllowedActions.Select(action => action.ToString()).ToList(),
            workOrder.CompletedUtc,
            transitionedUtc);
    }
}
