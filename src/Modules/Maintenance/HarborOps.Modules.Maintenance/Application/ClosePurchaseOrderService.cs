using System.Data;
using System.Security.Cryptography;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

// Closes the undelivered balance of a partially received order. Receipts, stock, and ordered quantities stay
// untouched; only the open outstanding quantity is recorded as closed without delivery.
public sealed class ClosePurchaseOrderService(DbContext dbContext, TimeProvider timeProvider)
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int DeadlockVictim = 1205;

    // A system-versioned row changed by a transaction that began later than this one.
    private const int TemporalPeriodConflict = 13535;

    public async Task<ClosePurchaseOrderResponse> CloseAsync(
        Guid purchaseOrderId,
        ClosePurchaseOrderRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ClosePurchaseOrderValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ClosePurchaseOrderValidationException(errors);
        }

        var clientRequestId = request.ClientRequestId!.Value;
        var requestHash = ComputeRequestHash(purchaseOrderId, request.Reason!);

        try
        {
            return await CloseInTransactionAsync(
                purchaseOrderId,
                request,
                clientRequestId,
                requestHash,
                actorUserId,
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            // The failed transaction is already rolled back here. A competing request with the same client
            // request id may have closed the order first; report its result instead of failing.
            dbContext.ChangeTracker.Clear();

            var existing = await FindByClientRequestIdAsync(clientRequestId, cancellationToken);

            if (existing is not null)
            {
                return await ReplayAsync(purchaseOrderId, existing, clientRequestId, requestHash, cancellationToken);
            }

            throw new PurchaseOrderClosureConcurrencyException(
                purchaseOrderId,
                await ReadPreviewAsync(purchaseOrderId, cancellationToken));
        }
    }

    private async Task<ClosePurchaseOrderResponse> CloseInTransactionAsync(
        Guid purchaseOrderId,
        ClosePurchaseOrderRequest request,
        Guid clientRequestId,
        string requestHash,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        // Serializable, and the order's concurrency token is replaced, so a close racing a receipt, a
        // cancellation, or another close cannot both commit.
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        // A retried close returns the recorded closure.
        var existing = await FindByClientRequestIdAsync(clientRequestId, cancellationToken);

        if (existing is not null)
        {
            return await ReplayAsync(purchaseOrderId, existing, clientRequestId, requestHash, cancellationToken);
        }

        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .SingleOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (purchaseOrder is null)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        var orderLines = await dbContext.Set<PurchaseOrderLine>()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToListAsync(cancellationToken);

        var evaluation = await PurchaseOrderClosureEvaluator.EvaluateAsync(
            dbContext,
            purchaseOrder,
            orderLines,
            cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (request.ExpectedConcurrencyToken != purchaseOrder.ConcurrencyToken)
        {
            throw new PurchaseOrderClosureConcurrencyException(
                purchaseOrderId,
                PurchaseOrderClosureEvaluator.ToPreview(purchaseOrder, evaluation, utcNow));
        }

        if (!string.Equals(evaluation.PreviewToken, request.PreviewToken, StringComparison.OrdinalIgnoreCase))
        {
            throw new PurchaseOrderClosureStalePreviewException(
                purchaseOrderId,
                PurchaseOrderClosureEvaluator.ToPreview(purchaseOrder, evaluation, utcNow));
        }

        if (evaluation.Blockers.Count > 0)
        {
            throw new PurchaseOrderClosureBlockedException(evaluation.Blockers);
        }

        var statusBefore = purchaseOrder.Status;

        foreach (var line in orderLines)
        {
            line.CloseOutstanding();
        }

        purchaseOrder.RecordClosure(orderLines);

        var closure = PurchaseOrderClosure.Create(
            purchaseOrder,
            statusBefore,
            clientRequestId,
            requestHash,
            request.Reason!,
            actorUserId,
            utcNow);

        var closureLines = orderLines
            .OrderBy(line => line.Id)
            .Select(line => PurchaseOrderClosureLine.Create(
                closure,
                line,
                evaluation.ReturnedQuantities.GetValueOrDefault(line.Id)))
            .ToList();

        await dbContext.Set<PurchaseOrderClosure>().AddAsync(closure, cancellationToken);
        await dbContext.Set<PurchaseOrderClosureLine>().AddRangeAsync(closureLines, cancellationToken);

        // One SaveChangesAsync: the closure evidence, closed line quantities, and order status commit together.
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var details = await PurchaseOrderClosureReader.ReadDetailsAsync(dbContext, closure, cancellationToken);

        return new ClosePurchaseOrderResponse(
            details,
            false,
            purchaseOrder.Status.ToString(),
            purchaseOrder.ConcurrencyToken,
            PurchaseOrderReceiptReader.AllowedActions(purchaseOrder.Status));
    }

    private async Task<ClosePurchaseOrderResponse> ReplayAsync(
        Guid purchaseOrderId,
        PurchaseOrderClosure existing,
        Guid clientRequestId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (existing.PurchaseOrderId != purchaseOrderId
            || !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new PurchaseOrderClosureRequestConflictException(clientRequestId);
        }

        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .SingleAsync(order => order.Id == purchaseOrderId, cancellationToken);

        var details = await PurchaseOrderClosureReader.ReadDetailsAsync(dbContext, existing, cancellationToken);

        return new ClosePurchaseOrderResponse(
            details,
            true,
            purchaseOrder.Status.ToString(),
            purchaseOrder.ConcurrencyToken,
            PurchaseOrderReceiptReader.AllowedActions(purchaseOrder.Status));
    }

    private async Task<PurchaseOrderClosurePreview> ReadPreviewAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .SingleAsync(order => order.Id == purchaseOrderId, cancellationToken);

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

    private Task<PurchaseOrderClosure?> FindByClientRequestIdAsync(
        Guid clientRequestId,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<PurchaseOrderClosure>()
            .AsNoTracking()
            .SingleOrDefaultAsync(closure => closure.ClientRequestId == clientRequestId, cancellationToken);
    }

    // The fingerprint covers what the client asked for, not the tokens it was confirmed against, so an
    // identical retry still matches after the order has been closed.
    private static string ComputeRequestHash(Guid purchaseOrderId, string reason)
    {
        var fingerprint = new StringBuilder()
            .Append("close")
            .Append('|').Append(purchaseOrderId)
            .Append('|').Append(reason.Trim());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
    }

    // Stale tokens, a duplicate client request id or second closure, serializable deadlocks, and temporal
    // period clashes all mean another request won the race. The whole chain is inspected because the
    // execution strategy wraps deadlocks raised by queries.
    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException
                || current is SqlException
                {
                    Number: UniqueIndexViolation
                        or UniqueConstraintViolation
                        or DeadlockVictim
                        or TemporalPeriodConflict,
                })
            {
                return true;
            }
        }

        return false;
    }
}
