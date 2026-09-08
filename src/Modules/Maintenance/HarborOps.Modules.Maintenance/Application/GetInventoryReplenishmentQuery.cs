using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetInventoryReplenishmentQuery(DbContext dbContext)
{
    public async Task<GetInventoryReplenishmentResponse> GetReplenishmentAsync(
        GetInventoryReplenishmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetInventoryReplenishmentValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetInventoryReplenishmentValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetInventoryReplenishmentValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetInventoryReplenishmentValidator.DefaultPageSize;

        var reservations = dbContext.Set<WorkOrderPartReservation>().AsNoTracking();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>().AsNoTracking();
        var purchaseOrders = dbContext.Set<PurchaseOrder>().AsNoTracking();
        var purchaseOrderLines = dbContext.Set<PurchaseOrderLine>().AsNoTracking();

        var candidates =
            from item in dbContext.Set<InventoryItem>().AsNoTracking()
            join supplier in dbContext.Set<Supplier>().AsNoTracking()
                on item.SupplierId equals supplier.Id
            let reservedQuantity = reservations
                .Where(reservation => reservation.InventoryItemId == item.Id
                    && workOrders.Any(workOrder => workOrder.Id == reservation.WorkOrderId
                        && (workOrder.Status == WorkOrderStatus.Open
                            || workOrder.Status == WorkOrderStatus.InProgress)))
                .Sum(reservation => reservation.QuantityReserved - reservation.QuantityConsumed)
            let openPurchaseOrderQuantity = purchaseOrderLines
                .Where(line => line.PartId == item.Id
                    && purchaseOrders.Any(order => order.Id == line.PurchaseOrderId
                        && (order.Status == PurchaseOrderStatus.Draft
                            || order.Status == PurchaseOrderStatus.Ordered)))
                .Sum(line => line.QuantityOrdered)
            where item.Quantity - reservedQuantity < item.MinimumQuantity
            select new
            {
                item.Id,
                item.Name,
                item.PartNumber,
                item.SupplierId,
                SupplierName = supplier.Name,
                StockOnHand = item.Quantity,
                ReorderLevel = item.MinimumQuantity,
                ReservedQuantity = reservedQuantity,
                AvailableQuantity = item.Quantity - reservedQuantity,
                ShortageQuantity = item.MinimumQuantity - (item.Quantity - reservedQuantity),
                OpenPurchaseOrderQuantity = openPurchaseOrderQuantity,
            };

        var totalCount = await candidates.CountAsync(cancellationToken);

        var rows = await candidates
            .OrderByDescending(row => row.ShortageQuantity)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.PartNumber)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new InventoryReplenishmentListItem(
                row.Id,
                row.Name,
                row.PartNumber,
                row.SupplierId,
                row.SupplierName,
                row.StockOnHand,
                row.ReservedQuantity,
                row.AvailableQuantity,
                row.ReorderLevel,
                row.ShortageQuantity,
                row.OpenPurchaseOrderQuantity,
                row.OpenPurchaseOrderQuantity >= row.ShortageQuantity
                    ? InventoryReplenishmentClassifications.Covered
                    : row.AvailableQuantity <= 0
                        ? InventoryReplenishmentClassifications.Critical
                        : InventoryReplenishmentClassifications.Low))
            .ToListAsync(cancellationToken);

        return new GetInventoryReplenishmentResponse(rows, pageNumber, pageSize, totalCount);
    }
}
