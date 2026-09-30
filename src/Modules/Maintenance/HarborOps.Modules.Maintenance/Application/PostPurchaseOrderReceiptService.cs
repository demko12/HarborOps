using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class PostPurchaseOrderReceiptService(DbContext dbContext, TimeProvider timeProvider)
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int DeadlockVictim = 1205;

    // A system-versioned row changed by a transaction that began later than this one.
    private const int TemporalPeriodConflict = 13535;

    private sealed record ReceiptCommand(
        Guid ClientRequestId,
        Guid? ExpectedConcurrencyToken,
        string? PreviewToken,
        DateTime? ReceivedUtc,
        string? DeliveryReference,
        string? Notes,
        // Null means "receive whatever is still outstanding" at posting time.
        IReadOnlyList<PurchaseOrderReceiptLineEntry>? Lines,
        string RequestHash);

    public async Task<PostPurchaseOrderReceiptResponse> PostAsync(
        Guid purchaseOrderId,
        PostPurchaseOrderReceiptRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PostPurchaseOrderReceiptValidator.Validate(request, timeProvider.GetUtcNow().UtcDateTime);
        if (errors.Count > 0)
        {
            throw new PostPurchaseOrderReceiptValidationException(errors);
        }

        var command = new ReceiptCommand(
            request.ClientRequestId!.Value,
            request.ExpectedConcurrencyToken,
            request.PreviewToken,
            request.ReceivedUtc,
            request.DeliveryReference,
            request.Notes,
            request.Lines!,
            ComputeRequestHash(
                purchaseOrderId,
                request.ReceivedUtc,
                request.DeliveryReference,
                request.Notes,
                request.Lines!));

        return await PostCommandAsync(purchaseOrderId, command, actorUserId, cancellationToken);
    }

    public async Task<PostPurchaseOrderReceiptResponse> ReceiveRemainingAsync(
        Guid purchaseOrderId,
        ReceiveRemainingPurchaseOrderRequest? request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        request ??= new ReceiveRemainingPurchaseOrderRequest();

        var errors = ReceiveRemainingPurchaseOrderValidator.Validate(request, timeProvider.GetUtcNow().UtcDateTime);
        if (errors.Count > 0)
        {
            throw new ReceiveRemainingPurchaseOrderValidationException(errors);
        }

        // Without a client request id the call cannot be replayed, but a retry still cannot double-post:
        // it finds nothing outstanding and is rejected.
        var command = new ReceiptCommand(
            request.ClientRequestId ?? Guid.NewGuid(),
            request.ExpectedConcurrencyToken,
            request.PreviewToken,
            request.ReceivedUtc,
            request.DeliveryReference,
            request.Notes,
            null,
            ComputeRequestHash(
                purchaseOrderId,
                request.ReceivedUtc,
                request.DeliveryReference,
                request.Notes,
                null));

        return await PostCommandAsync(purchaseOrderId, command, actorUserId, cancellationToken);
    }

    internal static ChangePurchaseOrderStatusResponse ToStatusChangeResponse(PostPurchaseOrderReceiptResponse response)
    {
        var stockAdjustments = response.Receipt.Movements
            .GroupBy(movement => movement.InventoryItemId)
            .OrderBy(group => group.Key)
            .Select(group => new PurchaseOrderStockAdjustment(
                group.Key,
                group.Sum(movement => movement.QuantityDelta),
                group.Max(movement => movement.QuantityAfter)))
            .ToList();

        return new ChangePurchaseOrderStatusResponse(
            response.Receipt.PurchaseOrderId,
            response.Receipt.OrderStatusBefore,
            response.OrderStatus,
            PurchaseOrder.AllowedTransitions(Enum.Parse<PurchaseOrderStatus>(response.OrderStatus))
                .Select(status => status.ToString())
                .ToList(),
            stockAdjustments,
            response.Receipt,
            response.AlreadyPosted);
    }

    private async Task<PostPurchaseOrderReceiptResponse> PostCommandAsync(
        Guid purchaseOrderId,
        ReceiptCommand command,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await PostInTransactionAsync(purchaseOrderId, command, actorUserId, cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            // The failed transaction is already rolled back here. A competing request with the same client
            // request id may have posted first; report its result instead of failing.
            dbContext.ChangeTracker.Clear();

            var existing = await FindByClientRequestIdAsync(command.ClientRequestId, cancellationToken);

            if (existing is not null)
            {
                return await ReplayAsync(purchaseOrderId, existing, command, cancellationToken);
            }

            throw new PurchaseOrderReceiptConcurrencyException(purchaseOrderId);
        }
    }

    private async Task<PostPurchaseOrderReceiptResponse> PostInTransactionAsync(
        Guid purchaseOrderId,
        ReceiptCommand command,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        // A retried post returns the recorded receipt and never moves stock a second time.
        var existing = await FindByClientRequestIdAsync(command.ClientRequestId, cancellationToken);

        if (existing is not null)
        {
            return await ReplayAsync(purchaseOrderId, existing, command, cancellationToken);
        }

        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .SingleOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (purchaseOrder is null)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        if (command.ExpectedConcurrencyToken is { } expectedToken && expectedToken != purchaseOrder.ConcurrencyToken)
        {
            throw new PurchaseOrderReceiptConcurrencyException(purchaseOrderId);
        }

        var orderLines = await dbContext.Set<PurchaseOrderLine>()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToDictionaryAsync(line => line.Id, cancellationToken);

        IReadOnlyDictionary<Guid, decimal> requestedQuantities;

        if (command.Lines is null)
        {
            requestedQuantities = PurchaseOrderReceiptPostingEvaluator.Outstanding(orderLines.Values);
        }
        else
        {
            var lineErrors = PurchaseOrderReceiptLinesValidator.ValidateAgainstOrder(command.Lines, orderLines);
            if (lineErrors.Count > 0)
            {
                throw new PostPurchaseOrderReceiptValidationException(lineErrors);
            }

            requestedQuantities = command.Lines.ToDictionary(
                entry => entry.PurchaseOrderLineId!.Value,
                entry => entry.Quantity!.Value);
        }

        // Items are tracked so their stock token guards the update against concurrent usage, stocktakes,
        // supplier returns, and other receipts of the same part.
        var receivedLines = orderLines.Values
            .Where(line => requestedQuantities.ContainsKey(line.Id))
            .ToList();
        var inventoryItems = await PurchaseOrderReceiptReader.ReadItemsAsync(
            dbContext,
            receivedLines,
            true,
            cancellationToken);

        var evaluation = await PurchaseOrderReceiptPostingEvaluator.EvaluateAsync(
            dbContext,
            purchaseOrder,
            orderLines.Values,
            requestedQuantities,
            inventoryItems,
            cancellationToken);

        if (command.PreviewToken is not null
            && !string.Equals(evaluation.PreviewToken, command.PreviewToken, StringComparison.OrdinalIgnoreCase))
        {
            throw new PurchaseOrderReceiptStalePreviewException(purchaseOrderId);
        }

        if (evaluation.Blockers.Count > 0)
        {
            throw new PurchaseOrderReceiptPostingBlockedException(evaluation.Blockers);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusBefore = purchaseOrder.Status;

        foreach (var line in receivedLines)
        {
            line.Receive(requestedQuantities[line.Id]);
        }

        purchaseOrder.RecordReceipt(orderLines.Values);

        var receipt = PurchaseOrderReceipt.Create(
            purchaseOrder,
            statusBefore,
            command.ClientRequestId,
            command.RequestHash,
            command.ReceivedUtc ?? utcNow,
            command.DeliveryReference,
            command.Notes,
            actorUserId,
            utcNow);

        var receiptLines = new List<PurchaseOrderReceiptLine>(receivedLines.Count);
        var movements = new List<InventoryAdjustment>(receivedLines.Count);

        foreach (var group in receivedLines.GroupBy(line => line.PartId).OrderBy(group => group.Key))
        {
            var inventoryItem = inventoryItems[group.Key];
            var quantityBefore = inventoryItem.Quantity;

            // Stock is increased once per item; each line still gets its own attributable movement.
            inventoryItem.IncreaseStock(group.Sum(line => requestedQuantities[line.Id]));

            foreach (var line in group.OrderBy(line => line.Id))
            {
                var receiptLine = PurchaseOrderReceiptLine.Create(receipt, line, requestedQuantities[line.Id]);
                var movement = InventoryAdjustment.CreateFromPurchaseOrderReceipt(
                    receipt,
                    receiptLine,
                    quantityBefore,
                    actorUserId,
                    utcNow);

                receiptLines.Add(receiptLine);
                movements.Add(movement);
                quantityBefore = movement.QuantityAfter;
            }
        }

        await dbContext.Set<PurchaseOrderReceipt>().AddAsync(receipt, cancellationToken);
        await dbContext.Set<PurchaseOrderReceiptLine>().AddRangeAsync(receiptLines, cancellationToken);
        await dbContext.Set<InventoryAdjustment>().AddRangeAsync(movements, cancellationToken);

        // One SaveChangesAsync: the receipt, stock movements, line totals, and order status commit together.
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var details = await PurchaseOrderReceiptReader.ReadDetailsAsync(dbContext, [receipt], cancellationToken);

        return new PostPurchaseOrderReceiptResponse(
            details[0],
            false,
            purchaseOrder.Status.ToString(),
            purchaseOrder.ConcurrencyToken,
            PurchaseOrderReceiptReader.AllowedActions(purchaseOrder.Status));
    }

    private async Task<PostPurchaseOrderReceiptResponse> ReplayAsync(
        Guid purchaseOrderId,
        PurchaseOrderReceipt existing,
        ReceiptCommand command,
        CancellationToken cancellationToken)
    {
        if (existing.PurchaseOrderId != purchaseOrderId
            || !string.Equals(existing.RequestHash, command.RequestHash, StringComparison.Ordinal))
        {
            throw new PurchaseOrderReceiptRequestConflictException(command.ClientRequestId);
        }

        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .SingleAsync(order => order.Id == purchaseOrderId, cancellationToken);

        var details = await PurchaseOrderReceiptReader.ReadDetailsAsync(dbContext, [existing], cancellationToken);

        return new PostPurchaseOrderReceiptResponse(
            details[0],
            true,
            purchaseOrder.Status.ToString(),
            purchaseOrder.ConcurrencyToken,
            PurchaseOrderReceiptReader.AllowedActions(purchaseOrder.Status));
    }

    private Task<PurchaseOrderReceipt?> FindByClientRequestIdAsync(
        Guid clientRequestId,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<PurchaseOrderReceipt>()
            .AsNoTracking()
            .SingleOrDefaultAsync(receipt => receipt.ClientRequestId == clientRequestId, cancellationToken);
    }

    // The fingerprint covers what the client asked for, not what the server derived from it, so an
    // identical retry of receive-remaining still matches after the order has been fully received.
    private static string ComputeRequestHash(
        Guid purchaseOrderId,
        DateTime? receivedUtc,
        string? deliveryReference,
        string? notes,
        IReadOnlyList<PurchaseOrderReceiptLineEntry>? lines)
    {
        var fingerprint = new StringBuilder()
            .Append(lines is null ? "remaining" : "lines")
            .Append('|').Append(purchaseOrderId)
            .Append('|').Append(receivedUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append('|').Append(PurchaseOrderReceipt.Normalize(deliveryReference))
            .Append('|').Append(PurchaseOrderReceipt.Normalize(notes));

        foreach (var line in (lines ?? []).OrderBy(line => line.PurchaseOrderLineId))
        {
            fingerprint
                .Append('|').Append(line.PurchaseOrderLineId)
                .Append(':').Append(line.Quantity!.Value.ToString("0.000", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
    }

    // Stale tokens, a duplicate client request id, serializable deadlocks, and temporal period clashes all mean
    // another request won the race. The execution strategy wraps deadlocks raised by queries, so the whole
    // chain is inspected; otherwise such a race would surface as a server error instead of a conflict.
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
