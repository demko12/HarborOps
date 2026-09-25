using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal static class SupplierReturnReader
{
    internal static async Task<SupplierReturnDetails?> ReadDetailsAsync(
        DbContext dbContext,
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        var supplierReturn = await dbContext.Set<SupplierReturn>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == supplierReturnId, cancellationToken);

        if (supplierReturn is null)
        {
            return null;
        }

        var purchaseOrderStatus = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(order => order.Id == supplierReturn.PurchaseOrderId)
            .Select(order => order.Status)
            .SingleAsync(cancellationToken);

        var supplierName = await dbContext.Set<Supplier>()
            .AsNoTracking()
            .Where(supplier => supplier.Id == supplierReturn.SupplierId)
            .Select(supplier => supplier.Name)
            .SingleOrDefaultAsync(cancellationToken);

        var lines = await dbContext.Set<SupplierReturnLine>()
            .AsNoTracking()
            .Where(line => line.SupplierReturnId == supplierReturnId)
            .ToListAsync(cancellationToken);

        var itemIds = lines.Select(line => line.InventoryItemId).Distinct().ToList();

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var evaluation = await SupplierReturnPostingEvaluator.EvaluateAsync(
            dbContext,
            supplierReturn,
            purchaseOrderStatus,
            lines,
            inventoryItems,
            cancellationToken);

        var movements = await ReadMovementsAsync(dbContext, supplierReturnId, cancellationToken);

        return new SupplierReturnDetails(
            supplierReturn.Id,
            supplierReturn.PurchaseOrderId,
            purchaseOrderStatus.ToString(),
            supplierReturn.SupplierId,
            supplierName ?? string.Empty,
            supplierReturn.Status.ToString(),
            supplierReturn.Notes,
            supplierReturn.ConcurrencyToken,
            supplierReturn.CreatedByUserId,
            supplierReturn.CreatedUtc,
            supplierReturn.LastModifiedByUserId,
            supplierReturn.LastModifiedUtc,
            supplierReturn.PostedByUserId,
            supplierReturn.PostedUtc,
            supplierReturn.CancelledByUserId,
            supplierReturn.CancelledUtc,
            supplierReturn.CancellationReason,
            evaluation.Lines,
            evaluation.Blockers,
            movements);
    }

    internal static async Task<IReadOnlyList<SupplierReturnMovementItem>> ReadMovementsAsync(
        DbContext dbContext,
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<InventoryAdjustment>()
            .AsNoTracking()
            .Where(adjustment => adjustment.SupplierReturnId == supplierReturnId)
            .OrderBy(adjustment => adjustment.InventoryItemId)
            .ThenByDescending(adjustment => adjustment.QuantityBefore)
            .ThenBy(adjustment => adjustment.Id)
            .Select(adjustment => new SupplierReturnMovementItem(
                adjustment.Id,
                adjustment.InventoryItemId,
                adjustment.SupplierReturnId!.Value,
                adjustment.SupplierReturnLineId!.Value,
                adjustment.QuantityBefore,
                adjustment.QuantityAfter,
                adjustment.QuantityDelta,
                adjustment.Reason,
                adjustment.ActorUserId,
                adjustment.AdjustedUtc))
            .ToListAsync(cancellationToken);
    }

    // Sums what posted returns have already taken from each purchase-order line. Draft and cancelled
    // returns are ignored because they never consume the returnable allowance.
    internal static async Task<IReadOnlyDictionary<Guid, decimal>> ReadPostedQuantitiesAsync(
        DbContext dbContext,
        IReadOnlyCollection<Guid> purchaseOrderLineIds,
        Guid? excludedSupplierReturnId,
        CancellationToken cancellationToken)
    {
        var lineIds = purchaseOrderLineIds.ToList();

        return await (
            from line in dbContext.Set<SupplierReturnLine>().AsNoTracking()
            join supplierReturn in dbContext.Set<SupplierReturn>().AsNoTracking()
                on line.SupplierReturnId equals supplierReturn.Id
            where lineIds.Contains(line.PurchaseOrderLineId)
                && supplierReturn.Status == SupplierReturnStatus.Posted
                && supplierReturn.Id != excludedSupplierReturnId
            group line by line.PurchaseOrderLineId into posted
            select new
            {
                PurchaseOrderLineId = posted.Key,
                Quantity = posted.Sum(line => line.Quantity),
            })
            .ToDictionaryAsync(row => row.PurchaseOrderLineId, row => row.Quantity, cancellationToken);
    }
}
