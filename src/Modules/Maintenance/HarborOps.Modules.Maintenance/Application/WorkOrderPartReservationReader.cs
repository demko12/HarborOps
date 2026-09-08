using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal static class WorkOrderPartReservationReader
{
    internal static async Task<IReadOnlyList<WorkOrderPartReservationListItem>> ReadAsync(
        DbContext dbContext,
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var activeHolds =
            from reservation in dbContext.Set<WorkOrderPartReservation>().AsNoTracking()
            join workOrder in dbContext.Set<MaintenanceWorkOrder>().AsNoTracking()
                on reservation.WorkOrderId equals workOrder.Id
            where workOrder.Status == WorkOrderStatus.Open
                || workOrder.Status == WorkOrderStatus.InProgress
            select new
            {
                reservation.InventoryItemId,
                Held = reservation.QuantityReserved - reservation.QuantityConsumed,
            };

        return await (
            from reservation in dbContext.Set<WorkOrderPartReservation>().AsNoTracking()
            join item in dbContext.Set<InventoryItem>().AsNoTracking()
                on reservation.InventoryItemId equals item.Id
            where reservation.WorkOrderId == workOrderId
            orderby item.Name, item.PartNumber, item.Id
            select new WorkOrderPartReservationListItem(
                reservation.InventoryItemId,
                item.Name,
                item.PartNumber,
                reservation.QuantityRequested,
                reservation.QuantityReserved,
                reservation.QuantityConsumed,
                reservation.QuantityReserved - reservation.QuantityConsumed,
                reservation.QuantityRequested - reservation.QuantityReserved,
                reservation.QuantityRequested > reservation.QuantityReserved,
                item.Quantity,
                item.Quantity - activeHolds
                    .Where(hold => hold.InventoryItemId == item.Id)
                    .Sum(hold => hold.Held)))
                .ToListAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<Guid>> ReadShortageItemIdsAsync(
        DbContext dbContext,
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .Where(reservation => reservation.WorkOrderId == workOrderId
                && reservation.QuantityRequested > reservation.QuantityReserved)
            .OrderBy(reservation => reservation.InventoryItemId)
            .Select(reservation => reservation.InventoryItemId)
            .ToListAsync(cancellationToken);
    }
}
