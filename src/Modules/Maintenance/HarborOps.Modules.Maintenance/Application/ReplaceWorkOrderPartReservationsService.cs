using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class ReplaceWorkOrderPartReservationsService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ReplaceWorkOrderPartReservationsResponse> ReplaceReservationsAsync(
        Guid workOrderId,
        ReplaceWorkOrderPartReservationsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ReplaceWorkOrderPartReservationsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ReplaceWorkOrderPartReservationsValidationException(errors);
        }

        var workOrder = await dbContext.Set<MaintenanceWorkOrder>()
            .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        workOrder.EnsureReservationsCanChange();

        var requestedQuantities = request.Reservations!
            .ToDictionary(entry => entry.InventoryItemId!.Value, entry => entry.Quantity!.Value);
        var inventoryItemIds = requestedQuantities.Keys.ToList();

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .Where(item => inventoryItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var stockOnHand = inventoryItems.ToDictionary(item => item.Id, item => item.Quantity);

        var missingInventoryItemIds = inventoryItemIds
            .Where(inventoryItemId => !stockOnHand.ContainsKey(inventoryItemId))
            .OrderBy(inventoryItemId => inventoryItemId)
            .ToList();

        if (missingInventoryItemIds.Count > 0)
        {
            throw new WorkOrderPartReservationItemsNotFoundException(missingInventoryItemIds);
        }

        var otherHolds = await (
            from reservation in dbContext.Set<WorkOrderPartReservation>().AsNoTracking()
            join order in dbContext.Set<MaintenanceWorkOrder>().AsNoTracking()
                on reservation.WorkOrderId equals order.Id
            where inventoryItemIds.Contains(reservation.InventoryItemId)
                && reservation.WorkOrderId != workOrderId
                && (order.Status == WorkOrderStatus.Open || order.Status == WorkOrderStatus.InProgress)
            group reservation by reservation.InventoryItemId into grouped
            select new
            {
                InventoryItemId = grouped.Key,
                Held = grouped.Sum(reservation =>
                    reservation.QuantityReserved - reservation.QuantityConsumed),
            })
            .ToDictionaryAsync(hold => hold.InventoryItemId, hold => hold.Held, cancellationToken);

        var reservationSet = dbContext.Set<WorkOrderPartReservation>();

        var existingReservations = await reservationSet
            .Where(reservation => reservation.WorkOrderId == workOrderId)
            .ToListAsync(cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var removed in existingReservations
            .Where(reservation => !requestedQuantities.ContainsKey(reservation.InventoryItemId)))
        {
            if (removed.QuantityConsumed > 0)
            {
                throw new WorkOrderPartReservationConflictException(
                    $"Reservation for item '{removed.InventoryItemId}' cannot be removed because {removed.QuantityConsumed} was already consumed.");
            }

            reservationSet.Remove(removed);
        }

        foreach (var (inventoryItemId, quantityRequested) in requestedQuantities)
        {
            var availableQuantity = stockOnHand[inventoryItemId]
                - (otherHolds.TryGetValue(inventoryItemId, out var held) ? held : 0m);

            var existing = existingReservations
                .SingleOrDefault(reservation => reservation.InventoryItemId == inventoryItemId);

            try
            {
                if (existing is null)
                {
                    await reservationSet.AddAsync(
                        WorkOrderPartReservation.Create(
                            workOrderId,
                            inventoryItemId,
                            quantityRequested,
                            availableQuantity,
                            utcNow),
                        cancellationToken);
                }
                else
                {
                    existing.ChangeRequestedQuantity(quantityRequested, availableQuantity, utcNow);
                }
            }
            catch (WorkOrderPartReservationInvariantException exception)
            {
                throw new WorkOrderPartReservationConflictException(exception.Message);
            }
        }

        var removedItemIds = existingReservations
            .Where(reservation => !requestedQuantities.ContainsKey(reservation.InventoryItemId))
            .Select(reservation => reservation.InventoryItemId)
            .ToList();

        inventoryItems.AddRange(await dbContext.Set<InventoryItem>()
            .Where(item => removedItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken));

        foreach (var inventoryItem in inventoryItems)
        {
            inventoryItem.MarkReservationsChanged();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var items = await WorkOrderPartReservationReader.ReadAsync(
            dbContext,
            workOrderId,
            cancellationToken);

        return new ReplaceWorkOrderPartReservationsResponse(
            workOrder.Id,
            workOrder.Status.ToString(),
            items,
            items.Any(item => item.HasShortage),
            utcNow);
    }
}

