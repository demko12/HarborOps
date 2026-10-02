using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

// Read-only: evaluates closing the undelivered balance with the closing rules without changing the order.
public sealed class GetPurchaseOrderClosurePreviewQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<PurchaseOrderClosurePreview> GetPreviewAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (purchaseOrder is null)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        var orderLines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToListAsync(cancellationToken);

        var evaluation = await PurchaseOrderClosureEvaluator.EvaluateAsync(
            dbContext,
            purchaseOrder,
            orderLines,
            cancellationToken);

        return PurchaseOrderClosureEvaluator.ToPreview(
            purchaseOrder,
            evaluation,
            timeProvider.GetUtcNow().UtcDateTime);
    }
}
