using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetSupplierReturnPostingPreviewQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<SupplierReturnPostingPreview> GetPreviewAsync(
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        var supplierReturn = await dbContext.Set<SupplierReturn>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == supplierReturnId, cancellationToken);

        if (supplierReturn is null)
        {
            throw new SupplierReturnNotFoundException(supplierReturnId);
        }

        supplierReturn.EnsureDraft();

        var purchaseOrderStatus = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(order => order.Id == supplierReturn.PurchaseOrderId)
            .Select(order => order.Status)
            .SingleAsync(cancellationToken);

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

        return new SupplierReturnPostingPreview(
            supplierReturn.Id,
            supplierReturn.ConcurrencyToken,
            evaluation.PreviewToken,
            evaluation.Blockers.Count == 0,
            evaluation.Lines,
            evaluation.Items,
            evaluation.Blockers,
            timeProvider.GetUtcNow().UtcDateTime);
    }
}
