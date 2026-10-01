using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetPurchaseOrderClosureQuery(DbContext dbContext)
{
    public async Task<PurchaseOrderClosureDetails> GetClosureAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .AnyAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (!exists)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        var closure = await dbContext.Set<PurchaseOrderClosure>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PurchaseOrderId == purchaseOrderId, cancellationToken);

        if (closure is null)
        {
            throw new PurchaseOrderClosureNotFoundException(purchaseOrderId);
        }

        return await PurchaseOrderClosureReader.ReadDetailsAsync(dbContext, closure, cancellationToken);
    }
}
