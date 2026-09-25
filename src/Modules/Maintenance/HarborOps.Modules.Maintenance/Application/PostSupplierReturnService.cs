using System.Data;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class PostSupplierReturnService(DbContext dbContext, TimeProvider timeProvider)
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<PostSupplierReturnResponse> PostAsync(
        Guid supplierReturnId,
        PostSupplierReturnRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PostSupplierReturnValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new PostSupplierReturnValidationException(errors);
        }

        try
        {
            return await PostInTransactionAsync(supplierReturnId, request, actorUserId, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The failed transaction is already rolled back here. A competing request may have posted
            // this return first; report its result instead of failing.
            dbContext.ChangeTracker.Clear();

            var current = await dbContext.Set<SupplierReturn>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == supplierReturnId, cancellationToken);

            if (current.Status == SupplierReturnStatus.Posted)
            {
                return await ReadPostedResultAsync(current, cancellationToken);
            }

            if (IsUniqueViolation(exception))
            {
                throw new SupplierReturnConcurrencyException(supplierReturnId);
            }

            throw;
        }
    }

    private async Task<PostSupplierReturnResponse> PostInTransactionAsync(
        Guid supplierReturnId,
        PostSupplierReturnRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var supplierReturn = await dbContext.Set<SupplierReturn>()
            .SingleOrDefaultAsync(candidate => candidate.Id == supplierReturnId, cancellationToken);

        if (supplierReturn is null)
        {
            throw new SupplierReturnNotFoundException(supplierReturnId);
        }

        // A retried post returns the recorded result and never moves stock a second time.
        if (supplierReturn.Status == SupplierReturnStatus.Posted)
        {
            return await ReadPostedResultAsync(supplierReturn, cancellationToken);
        }

        supplierReturn.EnsureDraft();

        if (supplierReturn.ConcurrencyToken != request.ExpectedConcurrencyToken)
        {
            throw new SupplierReturnConcurrencyException(supplierReturnId);
        }

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

        // Items are tracked so both stock and reservation tokens guard the update against concurrent
        // receipts, part usage, reservations, and stocktakes.
        var inventoryItems = await dbContext.Set<InventoryItem>()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var evaluation = await SupplierReturnPostingEvaluator.EvaluateAsync(
            dbContext,
            supplierReturn,
            purchaseOrderStatus,
            lines,
            inventoryItems,
            cancellationToken);

        if (!string.Equals(evaluation.PreviewToken, request.PreviewToken, StringComparison.OrdinalIgnoreCase))
        {
            throw new SupplierReturnStalePreviewException(supplierReturnId);
        }

        if (evaluation.Blockers.Count > 0)
        {
            throw new SupplierReturnPostingBlockedException(evaluation.Blockers);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var movements = new List<InventoryAdjustment>(lines.Count);

        foreach (var group in lines.GroupBy(line => line.InventoryItemId).OrderBy(group => group.Key))
        {
            var inventoryItem = inventoryItems[group.Key];
            var quantityBefore = inventoryItem.Quantity;

            // Stock is reduced once per item; each line still gets its own attributable movement.
            inventoryItem.ReduceStock(group.Sum(line => line.Quantity));

            foreach (var line in group.OrderBy(line => line.PurchaseOrderLineId))
            {
                var movement = InventoryAdjustment.CreateFromSupplierReturn(line, quantityBefore, actorUserId, utcNow);

                movements.Add(movement);
                quantityBefore = movement.QuantityAfter;
            }
        }

        supplierReturn.MarkPosted(lines, actorUserId, utcNow);

        await dbContext.Set<InventoryAdjustment>().AddRangeAsync(movements, cancellationToken);

        // One SaveChangesAsync: every stock movement and the posted status commit together.
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new PostSupplierReturnResponse(
            supplierReturn.Id,
            supplierReturn.Status.ToString(),
            supplierReturn.ConcurrencyToken,
            actorUserId,
            utcNow,
            false,
            await SupplierReturnReader.ReadMovementsAsync(dbContext, supplierReturn.Id, cancellationToken));
    }

    private async Task<PostSupplierReturnResponse> ReadPostedResultAsync(
        SupplierReturn supplierReturn,
        CancellationToken cancellationToken)
    {
        return new PostSupplierReturnResponse(
            supplierReturn.Id,
            supplierReturn.Status.ToString(),
            supplierReturn.ConcurrencyToken,
            supplierReturn.PostedByUserId!.Value,
            supplierReturn.PostedUtc!.Value,
            true,
            await SupplierReturnReader.ReadMovementsAsync(dbContext, supplierReturn.Id, cancellationToken));
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: UniqueIndexViolation or UniqueConstraintViolation,
        };
    }
}
