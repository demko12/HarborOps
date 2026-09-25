using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class UpdateSupplierReturnService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<SupplierReturnDetails> UpdateAsync(
        Guid supplierReturnId,
        UpdateSupplierReturnRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateSupplierReturnValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateSupplierReturnValidationException(errors);
        }

        var supplierReturn = await dbContext.Set<SupplierReturn>()
            .SingleOrDefaultAsync(candidate => candidate.Id == supplierReturnId, cancellationToken);

        if (supplierReturn is null)
        {
            throw new SupplierReturnNotFoundException(supplierReturnId);
        }

        supplierReturn.EnsureDraft();

        if (supplierReturn.ConcurrencyToken != request.ExpectedConcurrencyToken)
        {
            throw new SupplierReturnConcurrencyException(supplierReturnId);
        }

        var requestedLineIds = request.Lines!.Select(line => line.PurchaseOrderLineId!.Value).ToList();

        var purchaseOrderLines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == supplierReturn.PurchaseOrderId && requestedLineIds.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, cancellationToken);

        var postedQuantities = await SupplierReturnReader.ReadPostedQuantitiesAsync(
            dbContext,
            requestedLineIds,
            supplierReturnId,
            cancellationToken);

        var lineErrors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            request.Lines!,
            purchaseOrderLines,
            postedQuantities);

        if (lineErrors.Count > 0)
        {
            throw new UpdateSupplierReturnValidationException(lineErrors);
        }

        var existingLines = await dbContext.Set<SupplierReturnLine>()
            .Where(line => line.SupplierReturnId == supplierReturnId)
            .ToListAsync(cancellationToken);

        // Lines are matched by purchase-order line, so an unchanged line keeps its identity across edits.
        foreach (var existingLine in existingLines.Where(line => !requestedLineIds.Contains(line.PurchaseOrderLineId)))
        {
            dbContext.Set<SupplierReturnLine>().Remove(existingLine);
        }

        foreach (var entry in request.Lines!)
        {
            var purchaseOrderLineId = entry.PurchaseOrderLineId!.Value;
            var existingLine = existingLines.SingleOrDefault(line => line.PurchaseOrderLineId == purchaseOrderLineId);

            if (existingLine is null)
            {
                await dbContext.Set<SupplierReturnLine>().AddAsync(
                    SupplierReturnLine.Create(
                        supplierReturn,
                        purchaseOrderLines[purchaseOrderLineId],
                        entry.Quantity!.Value,
                        entry.Reason!),
                    cancellationToken);
            }
            else
            {
                existingLine.Change(entry.Quantity!.Value, entry.Reason!);
            }
        }

        supplierReturn.ChangeNotes(request.Notes, actorUserId, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await SupplierReturnReader.ReadDetailsAsync(dbContext, supplierReturnId, cancellationToken))!;
    }
}
