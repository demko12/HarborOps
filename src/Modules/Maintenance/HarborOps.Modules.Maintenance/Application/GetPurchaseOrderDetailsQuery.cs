using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetPurchaseOrderDetailsQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetPurchaseOrderDetailsResponse> GetDetailsAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var suppliers = dbContext.Set<Supplier>().AsNoTracking();

        var order = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == purchaseOrderId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.SupplierId,
                Supplier = suppliers
                    .Where(supplier => supplier.Id == candidate.SupplierId)
                    .Select(supplier => new
                    {
                        supplier.Name,
                        supplier.ContactPerson,
                        supplier.PhoneEmail,
                    })
                    .FirstOrDefault(),
                candidate.OrderDate,
                candidate.ExpectedDelivery,
                candidate.Status,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        var inventoryItems = dbContext.Set<InventoryItem>().AsNoTracking();

        var lines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .Select(line => new
            {
                line.Id,
                line.PartId,
                Item = inventoryItems
                    .Where(item => item.Id == line.PartId)
                    .Select(item => new { item.Name, item.PartNumber })
                    .FirstOrDefault(),
                line.QuantityOrdered,
                line.UnitCost,
            })
            .ToListAsync(cancellationToken);

        var lineItems = lines
            .Select(line => new PurchaseOrderLineItem(
                line.Id,
                line.PartId,
                line.Item?.Name ?? string.Empty,
                line.Item?.PartNumber ?? string.Empty,
                line.QuantityOrdered,
                line.UnitCost,
                line.QuantityOrdered * line.UnitCost))
            .OrderBy(line => line.InventoryItemName, StringComparer.Ordinal)
            .ThenBy(line => line.LineId)
            .ToList();

        var allowedTransitions = PurchaseOrder.AllowedTransitions(order.Status)
            .Select(status => status.ToString())
            .ToList();

        return new GetPurchaseOrderDetailsResponse(
            order.Id,
            order.SupplierId,
            order.Supplier?.Name ?? string.Empty,
            order.Supplier?.ContactPerson,
            order.Supplier?.PhoneEmail,
            order.OrderDate,
            order.ExpectedDelivery,
            order.Status.ToString(),
            PurchaseOrder.IsOverdue(order.ExpectedDelivery, order.Status, today),
            GetPurchaseOrdersQuery.DaysUntilDelivery(order.ExpectedDelivery, today),
            lineItems.Sum(line => line.LineTotal),
            allowedTransitions,
            lineItems);
    }
}
