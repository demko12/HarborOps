using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public class ReleaseWorkOrderPartReservationsService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task ReleaseAsync(Guid workOrderId, CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.Set<MaintenanceWorkOrder>()
                    .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        workOrder.EnsureReservationsCanRelease();

        var reservationSet = dbContext.Set<WorkOrderPartReservation>();

        var reservations = await reservationSet
            .Where(reservation => reservation.WorkOrderId == workOrderId)
            .ToListAsync(cancellationToken);

        if (reservations.Count == 0)
        {
            return;
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var reservation in reservations)
        {
            if (reservation.QuantityConsumed <= 0)
            {
                reservationSet.Remove(reservation);
            }
            else
            {
                reservation.ReleaseRemaining(utcNow);
            }
        }

        var inventoryItemIds = reservations.Select(reservation => reservation.InventoryItemId).ToList();

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .Where(item => inventoryItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        foreach (var inventoryItem in inventoryItems)
        {
            inventoryItem.MarkReservationsChanged();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
