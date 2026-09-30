using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CreateSupplierReturnService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<SupplierReturnDetails> CreateAsync(
        CreateSupplierReturnRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CreateSupplierReturnValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CreateSupplierReturnValidationException(errors);
        }

        var purchaseOrderId = request.PurchaseOrderId!.Value;

        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (purchaseOrder is null)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        if (!PurchaseOrder.CanReturn(purchaseOrder.Status))
        {
            throw new SupplierReturnPurchaseOrderNotReceivedException(purchaseOrderId, purchaseOrder.Status.ToString());
        }

        var requestedLineIds = request.Lines!.Select(line => line.PurchaseOrderLineId!.Value).ToList();

        var purchaseOrderLines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId && requestedLineIds.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, cancellationToken);

        var postedQuantities = await SupplierReturnReader.ReadPostedQuantitiesAsync(
            dbContext,
            requestedLineIds,
            null,
            cancellationToken);

        var lineErrors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            request.Lines!,
            purchaseOrderLines,
            postedQuantities);

        if (lineErrors.Count > 0)
        {
            throw new CreateSupplierReturnValidationException(lineErrors);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var supplierReturn = SupplierReturn.Create(purchaseOrder, request.Notes, actorUserId, utcNow);

        var lines = request.Lines!
            .Select(entry => SupplierReturnLine.Create(
                supplierReturn,
                purchaseOrderLines[entry.PurchaseOrderLineId!.Value],
                entry.Quantity!.Value,
                entry.Reason!))
            .ToList();

        await dbContext.Set<SupplierReturn>().AddAsync(supplierReturn, cancellationToken);
        await dbContext.Set<SupplierReturnLine>().AddRangeAsync(lines, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (await SupplierReturnReader.ReadDetailsAsync(dbContext, supplierReturn.Id, cancellationToken))!;
    }
}
