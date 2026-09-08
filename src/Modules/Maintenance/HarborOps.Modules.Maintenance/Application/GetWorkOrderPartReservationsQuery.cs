using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetWorkOrderPartReservationsQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetWorkOrderPartReservationsResponse> GetReservationsAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var status = await dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .Where(workOrder => workOrder.Id == workOrderId)
            .Select(workOrder => (WorkOrderStatus?)workOrder.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        var items = await WorkOrderPartReservationReader.ReadAsync(
            dbContext,
            workOrderId,
            cancellationToken);

        return new GetWorkOrderPartReservationsResponse(
            workOrderId,
            status.Value.ToString(),
            MaintenanceWorkOrder.AllowedActionsFor(status.Value)
                .Select(action => action.ToString())
                .ToList(),
            items,
            items.Any(item => item.HasShortage),
            timeProvider.GetUtcNow().UtcDateTime);

    }
}
