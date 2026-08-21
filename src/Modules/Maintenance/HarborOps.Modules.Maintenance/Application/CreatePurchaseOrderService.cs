using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CreatePurchaseOrderService(DbContext dbContext)
{
    public async Task<CreatePurchaseOrderResponse> CreateAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CreatePurchaseOrderValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CreatePurchaseOrderValidationException(errors);
        }

        var supplierId = request.SupplierId!.Value;

        var supplierExists = await dbContext.Set<Supplier>()
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == supplierId, cancellationToken);

        if (!supplierExists)
        {
            throw new SupplierNotFoundException(supplierId);
        }

        var requestedItemIds = request.Lines!
            .Select(line => line.InventoryItemId!.Value)
            .Distinct()
            .ToList();

        var knownItemIds = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => requestedItemIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        var missingItemIds = requestedItemIds
            .Where(itemId => !knownItemIds.Contains(itemId))
            .ToList();

        if (missingItemIds.Count > 0)
        {
            throw new PurchaseOrderItemsNotFoundException(missingItemIds);
        }

        var purchaseOrder = PurchaseOrder.Create(
            supplierId,
            request.OrderDate!.Value,
            request.ExpectedDelivery);

        var lines = request.Lines!
            .Select(line => PurchaseOrderLine.Create(
                purchaseOrder.Id,
                line.InventoryItemId!.Value,
                line.QuantityOrdered!.Value,
                line.UnitCost!.Value))
            .ToList();

        await dbContext.Set<PurchaseOrder>().AddAsync(purchaseOrder, cancellationToken);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(lines, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreatePurchaseOrderResponse(
            purchaseOrder.Id,
            purchaseOrder.Status.ToString(),
            lines.Count,
            PurchaseOrderLine.OrderTotal(lines));
    }
}
